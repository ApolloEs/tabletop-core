// The wire vocabulary, mirrored from src/Protocol (Wire constants and the
// golden-tested JSON shapes). If a name here drifts from the C# side, the
// golden tests are the arbiter — this file follows them, never the reverse.

export const HUB_PATH = "/game";
export const PROTOCOL_VERSION = 1;

// Hub -> client method names (what we subscribe to).
export const ON = {
  welcome: "welcome",
  lobby: "lobby",
  catalog: "catalog",
  state: "state",
  moveAccepted: "moveAccepted",
  moveRejected: "moveRejected",
  playerStatus: "playerStatus",
  error: "error",
};

// Client -> hub method names (what we invoke).
export const CALL = {
  createRoom: "createRoom",
  joinRoom: "joinRoom",
  resume: "resume",
  setConfig: "setConfig",
  start: "start",
  submitMove: "submitMove",
  endGame: "endGame",
};

// Move discriminators ("type" property, per the golden JSON).
export const MOVE = {
  playCard: "agony.playCard",
  drawCard: "agony.drawCard",
  passTurn: "agony.passTurn",
};

// --- Helpers over the golden payload shapes ---

export function zoneById(view, id) {
  return view.zones.find(z => z.id === id);
}

export function ownHand(view) {
  return view.zones.find(z => z.owner === view.viewer);
}

export function handOf(view, playerId) {
  return view.zones.find(z => z.owner === playerId);
}

export function isMyTurn(game) {
  return game.view.turn.activePlayer === game.view.viewer;
}

// The legal PlayCard variants for one card instance (a wild yields four,
// one per declarable color; a colored card yields one or none).
export function playVariantsFor(game, cardId) {
  return game.legalMoves.filter(m => m.type === MOVE.playCard && m.card === cardId);
}

export function canDraw(game) {
  return game.legalMoves.some(m => m.type === MOVE.drawCard);
}

export function canPass(game) {
  return game.legalMoves.some(m => m.type === MOVE.passTurn);
}
