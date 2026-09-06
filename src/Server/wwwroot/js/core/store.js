// The one place client-side state lives. Framework-agnostic by design:
// a future framework (Svelte, React, …) would subscribe to this exactly
// like render.js does today — the store and connection survive a framework
// migration untouched; only the subscriber changes. No DOM access here.

export function createStore() {
  const listeners = new Set();
  const state = {
    screen: "join",          // join | lobby | table
    connection: "idle",      // idle | connecting | connected | reconnecting | lost
    welcome: null,           // WelcomePayload
    lobby: null,             // LobbyPayload
    catalog: new Map(),      // CardDefinitionId -> CardCatalogEntry
    game: null,              // StatePayload (latest, version-guarded)
    seatStatus: new Map(),   // seat -> { connected, abandoned }
    lastError: null,         // ErrorPayload, cleared on next successful message
    awaitingAck: false,      // one-in-flight mirror, for disabling inputs
  };

  function publish() {
    for (const listener of listeners) listener(state);
  }

  return {
    get state() { return state; },

    subscribe(listener) {
      listeners.add(listener);
      listener(state);
      return () => listeners.delete(listener);
    },

    update(patch) {
      Object.assign(state, patch);
      publish();
    },

    // Message-shaped mutations, called by the connection layer.
    onWelcome(welcome) {
      state.welcome = welcome;
      state.lastError = null;
      publish();
    },
    onLobby(lobby) {
      state.lobby = lobby;
      state.screen = "lobby";
      state.lastError = null;
      publish();
    },
    onCatalog(catalog) {
      state.catalog = new Map(catalog.definitions.map(d => [d.id, d]));
      publish();
    },
    onGameState(game) {
      if (state.game && game.version < state.game.version) return; // stale
      state.game = game;
      state.screen = "table";
      state.lastError = null;
      publish();
    },
    onSeatStatus(status) {
      state.seatStatus.set(status.seat, {
        connected: status.connected,
        abandoned: status.abandoned,
      });
      publish();
    },
    onError(error) {
      state.lastError = error;
      publish();
    },
  };
}
