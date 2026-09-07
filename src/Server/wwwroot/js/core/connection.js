// The session layer over SignalR — everything the transport does NOT do.
// SignalR (injected, never imported: this module stays dependency-free and
// DOM-free) restores the *pipe*; this module restores the *seat*: it keeps
// the session token, resumes after reconnect, numbers moves, enforces the
// one-in-flight rule, and resends the unacked move so the server's dedupe
// can make delivery exactly-once. A future framework keeps this file.

import { HUB_PATH, ON, CALL } from "./protocol.js";

export function createGameConnection({ signalR, store, storage, baseUrl = "" }) {
  let token = null;
  let nextMoveId = 1;
  let inFlight = null; // MovePayload awaiting ack/rejection

  try { token = storage?.getItem("agony.sessionToken") ?? null; } catch { /* private mode */ }

  const hub = new signalR.HubConnectionBuilder()
    .withUrl(baseUrl + HUB_PATH)
    .withAutomaticReconnect()
    .build();

  hub.on(ON.welcome, w => {
    token = w.sessionToken;
    try { storage?.setItem("agony.sessionToken", w.sessionToken); } catch { /* best effort */ }
    // Resume the move-id sequence where this seat left off — a fresh page
    // starting back at 1 would collide with the server's dedupe.
    nextMoveId = Math.max(nextMoveId, w.lastMoveId + 1);
    store.onWelcome(w);
  });
  hub.on(ON.lobby, p => store.onLobby(p));
  hub.on(ON.catalog, p => store.onCatalog(p));
  hub.on(ON.state, p => {
    // A fresh game (finished -> playing) invalidates any move left in flight
    // from the previous one, so a rematch never starts out frozen.
    if (inFlight && store.state.game?.finished && !p.finished) clearInFlight();
    store.onGameState(p);
  });
  hub.on(ON.moveAccepted, a => {
    if (inFlight?.moveId === a.moveId) clearInFlight();
  });
  hub.on(ON.moveRejected, r => {
    if (inFlight?.moveId === r.moveId) clearInFlight();
    store.update({ lastError: { code: "moveRejected", message: r.error } });
  });
  hub.on(ON.playerStatus, p => store.onSeatStatus(p));
  hub.on(ON.error, e => store.onError(e));

  hub.onreconnecting(() => store.update({ connection: "reconnecting" }));
  hub.onreconnected(async () => {
    store.update({ connection: "connected" });
    // SignalR gave us a new connection id; reattach the seat, then resend
    // whatever was unacked when the pipe died (dedupe makes this safe).
    if (token) await hub.invoke(CALL.resume, token).catch(() => {});
    if (inFlight) await hub.invoke(CALL.submitMove, inFlight).catch(() => {});
  });
  hub.onclose(() => store.update({ connection: "lost" }));

  async function start() {
    store.update({ connection: "connecting" });
    await hub.start();
    store.update({ connection: "connected" });
  }

  function clearInFlight() {
    inFlight = null;
    store.update({ awaitingAck: false });
  }

  return {
    start,

    get savedToken() { return token; },

    /// On iOS Safari the connection dying on screen-lock is the COMMON
    /// case; the renderer calls this from visibilitychange so waking the
    /// phone reconnects proactively instead of waiting for a failed send.
    async ensureConnected() {
      if (hub.state === signalR.HubConnectionState.Disconnected) {
        await start();
        if (token) await hub.invoke(CALL.resume, token).catch(() => {});
        if (inFlight) await hub.invoke(CALL.submitMove, inFlight).catch(() => {});
      }
    },

    createRoom: name => hub.invoke(CALL.createRoom, name, "agony"),
    joinRoom: (code, name) => hub.invoke(CALL.joinRoom, code, name),
    resume: t => hub.invoke(CALL.resume, t ?? token),
    setConfig: config => hub.invoke(CALL.setConfig, config),
    startGame: () => hub.invoke(CALL.start),
    endGame: () => hub.invoke(CALL.endGame),
    rematch: () => hub.invoke(CALL.rematch),
    backToLobby: () => hub.invoke(CALL.backToLobby),

    /// One move in flight, ids monotonic — refuses (returns false) while a
    /// move is unacked rather than queueing, matching the console client.
    /// A failed send releases the slot: leaving it held would freeze every
    /// card in the hand until reload, since the renderer treats an unacked
    /// move as "inputs busy".
    async submitMove(move) {
      if (inFlight) return false;
      inFlight = { moveId: nextMoveId++, move };
      store.update({ awaitingAck: true });
      try {
        await hub.invoke(CALL.submitMove, inFlight);
      } catch (error) {
        // Reconnect resends whatever is still in flight, so a send that
        // failed because the pipe died stays pending on purpose; anything
        // else (a rejected invoke) releases the slot.
        if (hub.state === signalR.HubConnectionState.Connected) {
          clearInFlight();
          store.update({ lastError: { code: "sendFailed", message: "That move didn't reach the table — try again." } });
        }
        return false;
      }
      return true;
    },

    forgetSession() {
      token = null;
      try { storage?.removeItem("agony.sessionToken"); } catch { /* best effort */ }
    },
  };
}
