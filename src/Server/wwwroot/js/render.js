// The renderer — the ONLY layer that touches the DOM, and the only layer a
// future framework would replace. It reads the store, draws, and calls the
// connection's API; it owns zero game or session state of its own.

import {
  MOVE, COUNTER, zoneById, ownHand, isMyTurn,
  playVariantsFor, canDraw, canPass, pendingDraw, placingOf,
} from "./core/protocol.js";

const COLORS = ["red", "yellow", "green", "blue"];
const GLYPHS = {
  skip: "⊘", reverse: "⇄", draw2: "+2",
  swap: "⇆", rotate: "⟳", wild: "✦", wild4: "+4",
};
const ORDINALS = ["1st", "2nd", "3rd"];

const $ = id => document.getElementById(id);

export function initRenderer(store, connection) {
  let pickerCard = null; // wild instance awaiting a color declaration

  // --- one-time event wiring ---

  $("create").onclick = () => connection.createRoom($("name").value.trim());
  $("join").onclick = () => connection.joinRoom($("code").value.trim().toUpperCase(), $("name").value.trim());
  $("resume").onclick = () => connection.resume();
  $("start").onclick = () => connection.startGame();
  $("end-game").onclick = () => connection.endGame();
  $("again").onclick = () => connection.rematch();
  $("to-lobby").onclick = () => connection.backToLobby();
  $("pass").onclick = () => submitFirst(m => m.type === MOVE.passTurn);
  $("deck").onclick = () => submitFirst(m => m.type === MOVE.drawCard);
  $("take-debt").onclick = () => submitFirst(m => m.type === MOVE.drawCard);

  $("cfg-stack").onchange = $("cfg-swap").onchange = $("cfg-placings").onchange = () =>
    connection.setConfig({
      stackDrawCards: $("cfg-stack").checked,
      swapRotateCards: $("cfg-swap").checked,
      playForPlacings: $("cfg-placings").checked,
      jumpIn: false,
    });

  $("hand").onclick = event => {
    const button = event.target.closest("[data-card]");
    if (!button) return;
    const cardId = Number(button.dataset.card);
    const variants = playVariantsFor(store.state.game, cardId);
    if (variants.length === 0) return;
    if (variants.length === 1) { connection.submitMove(variants[0]); return; }
    pickerCard = cardId;            // a wild: four variants, ask for the color
    $("picker").hidden = false;
  };

  $("picker").onclick = event => {
    const pick = event.target.closest("[data-color]");
    if (!pick || pickerCard === null) return;
    const variant = playVariantsFor(store.state.game, pickerCard)
      .find(m => m.declaredColor === pick.dataset.color);
    closePicker();
    if (variant) connection.submitMove(variant);
  };
  $("picker-cancel").onclick = closePicker;

  function closePicker() { pickerCard = null; $("picker").hidden = true; }

  function submitFirst(predicate) {
    const move = store.state.game?.legalMoves.find(predicate);
    if (move) connection.submitMove(move);
  }

  // --- rendering (full redraw per store publish; trivial at this scale) ---

  store.subscribe(state => {
    $("screen-join").hidden = state.screen !== "join";
    $("screen-lobby").hidden = state.screen !== "lobby";
    $("screen-table").hidden = state.screen !== "table";
    $("resume").hidden = !(state.screen === "join" && connection.savedToken);

    renderBanner(state);
    renderToast(state);
    if (state.screen === "lobby" && state.lobby) renderLobby(state);
    if (state.screen === "table" && state.game) renderTable(state);
    $("result").hidden = !(state.screen === "table" && state.game?.finished);
  });

  function renderBanner(state) {
    const banner = $("banner");
    if (state.protocolMismatch) {
      const { page, server } = state.protocolMismatch;
      banner.textContent =
        `This page speaks protocol v${page} but the table is running v${server}. ` +
        `Restart the server, then reload — house rules will look broken until you do.`;
      banner.className = "lost";
      banner.hidden = false;
      return;
    }
    const messages = {
      reconnecting: "connection lost — reconnecting…",
      connecting: "connecting…",
      lost: "connection closed — reload to resume",
    };
    banner.textContent = messages[state.connection] ?? "";
    banner.className = state.connection === "lost" ? "lost" : "";
    banner.hidden = !messages[state.connection];
  }

  function renderToast(state) {
    const toast = $("toast");
    toast.hidden = !state.lastError;
    if (state.lastError) toast.textContent = state.lastError.message;
  }

  function renderLobby(state) {
    const { lobby, welcome } = state;
    $("room-code").textContent = welcome?.roomCode ?? "";
    $("lobby-players").innerHTML = lobby.players.map(p => `
      <li>
        <span class="dot ${p.connected ? "" : "off"}"></span>
        <span>${escapeHtml(p.name)}${p.isHost ? " ★" : ""}</span>
      </li>`).join("");

    const amHost = welcome?.seat === 0;
    $("cfg-stack").checked = lobby.config.stackDrawCards;
    $("cfg-swap").checked = lobby.config.swapRotateCards;
    $("cfg-placings").checked = lobby.config.playForPlacings;
    $("cfg-stack").disabled = $("cfg-swap").disabled = $("cfg-placings").disabled = !amHost;
    $("start").hidden = !amHost;
    $("start").disabled = !lobby.canStart;
    $("lobby-hint").textContent = amHost
      ? (lobby.canStart ? "" : "waiting for at least one more player…")
      : "waiting for the host to deal…";
  }

  function renderTable(state) {
    const { game, catalog, welcome, seatStatus } = state;
    const view = game.view;
    const mine = isMyTurn(game) && !game.finished;
    const sending = state.awaitingAck;
    // Two different reasons a card can't be tapped, and the player must be
    // able to tell them apart: it isn't your turn, or your move is in the air.
    const busy = sending || game.finished || !mine;
    const pending = pendingDraw(view);

    // Opponents strip (everyone but me, seat order).
    $("opponents").innerHTML = view.players
      .filter(p => p.id !== view.viewer)
      .map(p => {
        const status = seatStatus.get(p.seat) ?? { connected: true, abandoned: false };
        const dot = status.abandoned ? "gone" : status.connected ? "" : "off";
        const active = p.id === view.turn.activePlayer && !game.finished ? "active" : "";
        const placing = placingOf(game, p.id);
        const count = placing ? ORDINALS[placing - 1] ?? `${placing}th` : handCountOf(view, p.id);
        return `<div class="opponent ${active}">
          <span class="dot ${dot}"></span>
          <span>${escapeHtml(p.displayName)}</span>
          <span class="count">${count}</span>
        </div>`;
      }).join("");

    // Center: deck, discard top, table info.
    const deck = zoneById(view, "deck");
    $("deck-count").textContent = deck.cardCount;

    // Facing a debt, the normal draw is blocked and replaced by an explicit
    // "take" button that names the price — never auto-taken, even when the
    // player has nothing to answer with.
    const takeAvailable = mine && !sending && pending > 0 && canDraw(game);
    $("take-debt").hidden = !(mine && pending > 0);
    $("take-debt").textContent = `Take +${pending}`;
    $("take-debt").disabled = !takeAvailable;
    $("deck").disabled = pending > 0 || busy || !canDraw(game);

    const discard = zoneById(view, "discard");
    const top = discard.cards?.at(-1);
    $("discard").innerHTML = top ? cardFace(catalog.get(top.definition), "") : "";

    const table = zoneById(view, "table");
    const activeColor = COLORS[table.counters[COUNTER.activeColor] ?? 0];
    const arrow = view.turn.direction === "forward" ? "↻" : "↺";
    $("table-info").innerHTML = `
      <span class="info-chip"><span class="swatch c-${activeColor}"></span>${activeColor}</span>
      <span class="info-chip">${arrow} play direction</span>
      ${pending > 0 ? `<span class="info-chip debt">+${pending} pending</span>` : ""}`;

    // Turn banner — the single clearest signal on the screen.
    const banner = $("turn-banner");
    banner.className = mine ? "mine" : "";
    banner.textContent = game.finished ? ""
      : sending ? "sending…"
      : mine ? (pending > 0 ? `answer the +${pending} or take it` : "your turn")
      : `waiting for ${nameOf(view, view.turn.activePlayer)}…`;

    // My hand: playable cards raised and enabled; a whole idle hand is dimmed
    // so "not my turn" never reads as "the app broke".
    $("hand").classList.toggle("idle", !mine && !game.finished);
    $("hand").innerHTML = (ownHand(view).cards ?? []).map(card => {
      const playable = !busy && playVariantsFor(game, card.id).length > 0;
      return cardFace(catalog.get(card.definition), `data-card="${card.id}"`,
        playable ? "playable" : "", !playable);
    }).join("");

    $("pass").hidden = busy || !canPass(game);
    $("end-game").hidden = !(welcome?.seat === 0 && !game.finished &&
      [...seatStatus.values()].some(s => s.abandoned));

    if (game.finished) renderResult(state);
  }

  function renderResult(state) {
    const { game, welcome } = state;
    const view = game.view;
    const amHost = welcome?.seat === 0;
    const standings = game.standings;

    const myPlacing = placingOf(game, view.viewer);
    $("result-title").textContent =
      standings.length === 0 ? "The host ended the game"
      : myPlacing === 1 ? "You win! 🎉"
      : myPlacing ? `You finished ${ORDINALS[myPlacing - 1] ?? `${myPlacing}th`}`
      : `${nameOf(view, standings[0])} wins`;

    const rows = standings.map((playerId, index) => row(
      ORDINALS[index] ?? `${index + 1}th`, playerId));
    // Name the straggler only when exactly one player is still holding
    // cards; playing first-out-wins, several players never finish.
    if (standings.length > 0 && standings.length === view.players.length - 1) {
      const last = view.players.find(p => !standings.includes(p.id));
      if (last) rows.push(row("last", last.id));
    }
    $("result-standings").innerHTML = rows.join("");

    $("again").hidden = !amHost;
    $("to-lobby").hidden = !amHost;
    $("result-hint").textContent = amHost ? "" : "waiting for the host to deal again…";

    function row(place, playerId) {
      const me = playerId === view.viewer ? "me" : "";
      return `<li class="${me}"><span>${escapeHtml(nameOf(view, playerId))}</span><span class="place">${place}</span></li>`;
    }
  }

  function cardFace(entry, attrs, extraClass = "", disabled = false) {
    if (!entry) return "";
    if (entry.faceImage) // custom decks: the face is just a URL
      return `<button class="card ${extraClass}" ${attrs} ${disabled ? "disabled" : ""}><img src="${entry.faceImage}" alt="${escapeHtml(entry.name)}"></button>`;
    const color = entry.properties.color === "wild" ? "wild" : entry.properties.color;
    const glyph = entry.properties.number ?? GLYPHS[entry.properties.symbol] ?? "?";
    return `<button class="card c-${color} ${extraClass}" ${attrs} ${disabled ? "disabled" : ""} aria-label="${escapeHtml(entry.name)}">${glyph}</button>`;
  }

  function handCountOf(view, playerId) {
    return view.zones.find(z => z.owner === playerId)?.cardCount ?? 0;
  }

  function nameOf(view, playerId) {
    return view.players.find(p => p.id === playerId)?.displayName ?? "?";
  }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
  }
}
