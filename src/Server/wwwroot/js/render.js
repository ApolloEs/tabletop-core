// The renderer — the ONLY layer that touches the DOM, and the only layer a
// future framework would replace. It reads the store, draws, and calls the
// connection's API; it owns zero game or session state of its own.

import { zoneById, ownHand, isMyTurn, playVariantsFor, canDraw, canPass } from "./core/protocol.js";

const COLORS = ["red", "yellow", "green", "blue"];
const GLYPHS = {
  skip: "⊘", reverse: "⇄", draw2: "+2",
  swap: "⇆", rotate: "⟳", wild: "✦", wild4: "+4",
};

const $ = id => document.getElementById(id);

export function initRenderer(store, connection) {
  let pickerCard = null; // wild instance awaiting a color declaration

  // --- one-time event wiring ---

  $("create").onclick = () => connection.createRoom($("name").value.trim());
  $("join").onclick = () => connection.joinRoom($("code").value.trim().toUpperCase(), $("name").value.trim());
  $("resume").onclick = () => connection.resume();
  $("start").onclick = () => connection.startGame();
  $("end-game").onclick = () => connection.endGame();
  $("pass").onclick = () => submitFirst(m => m.type === "agony.passTurn");
  $("deck").onclick = () => submitFirst(m => m.type === "agony.drawCard");

  $("cfg-stack").onchange = $("cfg-swap").onchange = () =>
    connection.setConfig({
      stackDrawTwo: $("cfg-stack").checked,
      swapRotateCards: $("cfg-swap").checked,
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
  });

  function renderBanner(state) {
    const banner = $("banner");
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
    $("cfg-stack").checked = lobby.config.stackDrawTwo;
    $("cfg-swap").checked = lobby.config.swapRotateCards;
    $("cfg-stack").disabled = $("cfg-swap").disabled = !amHost;
    $("start").hidden = !amHost;
    $("start").disabled = !lobby.canStart;
    $("lobby-hint").textContent = amHost
      ? (lobby.canStart ? "" : "waiting for at least one more player…")
      : "waiting for the host to deal…";
  }

  function renderTable(state) {
    const { game, catalog, welcome, seatStatus } = state;
    const view = game.view;
    const busy = state.awaitingAck || game.finished;

    // Opponents strip (everyone but me, seat order).
    $("opponents").innerHTML = view.players
      .filter(p => p.id !== view.viewer)
      .map(p => {
        const status = seatStatus.get(p.seat) ?? { connected: true, abandoned: false };
        const dot = status.abandoned ? "gone" : status.connected ? "" : "off";
        const active = p.id === view.turn.activePlayer ? "active" : "";
        const count = handCountOf(view, p.id);
        return `<div class="opponent ${active}">
          <span class="dot ${dot}"></span>
          <span>${escapeHtml(p.displayName)}</span>
          <span class="count">${count}</span>
        </div>`;
      }).join("");

    // Center: deck, discard top, table info.
    const deck = zoneById(view, "deck");
    $("deck-count").textContent = deck.cardCount;
    $("deck").disabled = busy || !canDraw(game);

    const discard = zoneById(view, "discard");
    const top = discard.cards?.at(-1);
    $("discard").innerHTML = top ? cardFace(catalog.get(top.definition), "") : "";

    const table = zoneById(view, "table");
    const activeColor = COLORS[table.counters.activeColor ?? 0];
    const pending = table.counters.pendingDraw ?? 0;
    const arrow = view.turn.direction === "forward" ? "↻" : "↺";
    $("table-info").innerHTML = `
      <span class="info-chip"><span class="swatch c-${activeColor}"></span>${activeColor}</span>
      <span class="info-chip">${arrow} play direction</span>
      ${pending > 0 ? `<span class="info-chip debt">+${pending} pending</span>` : ""}`;

    // Turn banner.
    const mine = isMyTurn(game);
    const banner = $("turn-banner");
    banner.className = mine ? "mine" : "";
    banner.textContent = game.finished ? "" :
      mine ? "your turn" : `waiting for ${nameOf(view, view.turn.activePlayer)}…`;

    // My hand: playable cards raised and enabled.
    $("hand").innerHTML = (ownHand(view).cards ?? []).map(card => {
      const playable = !busy && playVariantsFor(game, card.id).length > 0;
      return cardFace(catalog.get(card.definition), `data-card="${card.id}"`,
        playable ? "playable" : "", !playable);
    }).join("");

    $("pass").hidden = busy || !canPass(game);
    $("end-game").hidden = !(welcome?.seat === 0 && !game.finished &&
      [...seatStatus.values()].some(s => s.abandoned));

    // Winner / ended overlay.
    document.querySelector(".winner")?.remove();
    if (game.finished) {
      const text = game.winner !== undefined && game.winner !== null
        ? (game.winner === view.viewer ? "You win! 🎉" : `${nameOf(view, game.winner)} wins`)
        : "The host ended the game";
      const overlay = document.createElement("div");
      overlay.className = "winner";
      overlay.textContent = text;
      document.body.appendChild(overlay);
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
