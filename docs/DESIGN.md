# Design

Why this project is shaped the way it is. The [README](../README.md) describes what exists and how the pieces fit; this document is the reasoning behind the calls, including the ones that constrain everything else.

---

## 1. The founding constraint

> Friends in a room. Mixed iPhones and Androids. Nobody installs anything.

That one scene decides most of the architecture. It rules out every client that needs provisioning, a store listing, or a cable:

- **Native iOS.** A free Apple account expires provisioning after 7 days and needs a Mac plus a cable per device. A paid account costs €99/year to solve a problem the web does not have.
- **Unity WebGL.** A heavy bundle and a poor mobile-Safari story; it was never blessed for phone browsers.
- **A web client.** Everyone opens a URL. No install, no store, no cables, and Android works the same as iOS.

**The client is a web client.** A native or Unity client stays possible later, but it is not the way into the room, so nothing is built for it today — with one exception, below.

The second constraint is that games should be *customisable* by the people playing them: custom card faces, and house rules argued over before the deal. That, not a rules language, is what "custom" means here (§5).

## 2. Why the engine is a separate, dependency-free library

`src/Engine` references no web framework, no game engine, and no JSON library. The compiler enforces it through project references, so a leak is a build error rather than a code review.

Three things fall out of that:

- **The rules are testable without a server.** The whole engine is exercised headlessly by xUnit. No HTTP, no sockets, no fixtures.
- **A client-side rewrite stays possible.** The same assembly could run inside a native client later, because it never learned where it was running.
- **The layers cannot quietly fuse.** `Protocol` is the only assembly that knows JSON exists; `Server` is the only one that knows about connections. When a wire concern shows up in a rule, it has nowhere to hide.

C# was chosen because it is fast, statically typed, has first-class realtime and test tooling, and is the one language where an engine can run both on a server and inside a game client without a rewrite. TypeScript was the serious alternative — sharing code between server and browser — and lost because card games are turn-based and low-frequency: there is no client-side prediction to share. The server just tells each client its legal moves, which is a small payload of plain data.

## 3. The engine model

**Cards are inert data.** A `CardDefinition` is an id, a display name, an optional face-image URL, and a property bag. Behaviour lives in the game class. This is what lets a custom deck be *content* rather than code.

**Zones are general containers, not lists of cards.** The instinct is `Dictionary<ZoneId, List<Card>>`, and it walls the project off from any game with a board. A zone is an ordered collection plus named counters, with a kind and a visibility. A deck, a hand, a discard pile, a poker pot and a board square are then all zones with different settings. Cheap to do now; a rewrite later.

**Every state change is an action.** `MoveCard`, `Draw`, `Shuffle`, `Flip`, `SetCounter` — each validates, then applies, then emits typed events (`CardPlayed`, `CardEnteredZone`, `TurnStarted`). Their setters are internal to the engine, so a game *cannot* mutate state without producing the matching event trail. The event vocabulary is the real API: renderers animate it, the projector redacts it, and data-driven card content would hook into it.

**Property reads go through one indirection.** Rules never read a property raw; they call `state.GetValue(card, "…")`. It costs nothing today and means a future modifier or buff layer intercepts a single method instead of every rule.

**Turn order is pluggable.** `ITurnSystem` answers "who acts now?". Round-robin with direction and skips ships in the engine; Agony swaps in its own, which steps over players who have already gone out.

## 4. Hidden information, from the second commit

A card game where the server can leak your hand is not a card game. Retrofitting secrecy means rewriting every message the server sends, so it was built in before there was a game to play.

- The server holds the only true `GameState`. Clients never receive it — they receive a `PlayerView`: their own hand in full, everyone else's as counts, plus the moves they are allowed to make.
- `StateProjector` filters state per viewer from zone visibility (`Public`, `OwnerOnly`, `Hidden`). `EventProjector` does the same to the event stream: others learn *that* you drew, never *what*, because a `CardsDrawnHidden` event replaces `CardDrawn`.
- **Card instance ids are seed-randomized.** Creating instances in generation order made "card #0" always the same card — and this source is public. Creation order is drawn from the seeded RNG instead, so ids carry no information while replays stay deterministic.
- **No behavioural tells.** Leaks also travel through what the system *does*. There is no auto-pass when a player has nothing playable: firing instantly would announce "nothing here", and not firing would announce "saving something". No hand-private data is attached to public zones.
- A test sweep plays entire games and asserts that no serialized payload ever names a card its recipient cannot see.

**Determinism is part of the same story.** Shuffles run server-side from a seed clients never see, using a hand-rolled PCG32 rather than `System.Random`, whose algorithm is not stable across .NET versions. Same seed plus same actions always reproduces the same game, which makes the shuffle auditable and replay trivial.

## 5. Scope boundaries that were chosen, not stumbled into

**No scripting layer in v1.** No Lua, no plugin loader, no untrusted-code sandbox, no mod API to version. Games are C# classes in this repo. The only game authors are the maintainer and perhaps a friend who codes, so an entire branch of the design evaporates.

**"Customisable" means house rules.** A config object passed to setup, with toggles in the lobby: stacking draw cards (a +2 answers a +2, a +4 answers anything), dedicated swap and rotate cards, and playing on for placings instead of stopping at the first player out. This is what people actually argue about before a game, and it costs a fraction of a scripting engine.

**Custom decks are v1 scope, not an enhancement.** The project began as "our own cards, on our phones", so a deck is a folder — `manifest.json`, `cards.json`, and an `images/` directory the server serves. A card face is a URL the client renders. Deck folders are gitignored and local to each clone, because the code is meant to be public and the pictures on the cards are not; that split has to be real before the repo goes public, so there is no history to scrub.

**The card editor is kept, and it is tier-1.** Composing a card on a phone is a web form: pick an image, type a name, choose from pre-built effect primitives, preview. No scripting runtime is required, because it composes primitives rather than authoring code. It is off the critical path, but the data model must not preclude it — which is why `EffectEntry` exists in the model, unread for now.

**Framework extraction is deferred on purpose.** You cannot invent the right abstractions from imagination. Build two or three concrete games that stress genuinely different mechanics, tolerate the duplication, then extract from what the duplication shows. The same rule bans a JSON or YAML rules DSL until real pain justifies it — a premature DSL is one of the most reliable ways a project like this dies.

## 6. The transport, and a decision that was reversed

The original call was raw `System.Net.WebSockets`, on the reasoning that a card game needs very little from a transport.

A throwaway spike hand-rolled that layer — message framing, per-socket send gates, keepalive, reconnect loops — measured it, and argued against its own result: those parts are undifferentiated plumbing, and their bugs are concurrency bugs, which are the most expensive kind to find. The decision was reversed to **SignalR**.

What SignalR does *not* provide stayed hand-built, because it is the part that carries the game:

- **Rooms are actors.** Every request becomes a command on a channel consumed by a single task that owns the `GameState`. Game logic is therefore single-threaded and lock-free, moves have a total order, and broadcasts cannot interleave. Even time-driven work — the disconnect sweep — posts a command through the same inbox instead of touching state from a timer thread.
- **A session is not a connection.** Reconnecting hands you a *new connection id*: SignalR restores the pipe, never the seat. Seats are session tokens that outlive connections, and `resume(token)` reattaches to one.
- **Moves are applied exactly once.** Clients choose move ids and the server dedupes, so a move in flight when the connection dies can be resent safely. An integration test kills the socket between the acknowledgement and its delivery to prove it.
- **The acting player is never named by the client.** It is derived from the connection server-side, so no client can act as someone else.

The accepted cost: a future native client would need work at the transport edge, since it would have to speak SignalR rather than a protocol of our own.

## 7. Rendering is per-game and pluggable

Renderers subscribe to the same event stream and are swappable per game.

- **Agony** uses DOM and CSS 3D transforms: `perspective` on the table, `rotateY` for flips, `translateZ` for stacking. It runs smoothly on a phone and ships in kilobytes with no build step.
- **A board game** would use a 2D WebGL renderer such as PixiJS — sprite batching, tweened token movement, particle effects — without the engine or the protocol noticing the difference.

## 8. The second game, and why it is not another card game

The planned second game is card-driven but board-based: cards are the action currency, while the real state lives on the board as token positions, possession and score.

It is second precisely because it violates the assumptions a shedding game lets you get away with. Two games that differ that violently shake out most of the API's real problems, and it is the direct test of the "zones are general containers" rule — `TokenMoved(player7, D4 → F6)` should be no harder to emit than `CardPlayed(redSeven)`. Today `ZoneKind.Grid` is an enum member with nothing behind it, deliberately, until that game demands cell and token structure.

## 9. Decisions currently locked

| Decision |
|---|
| Web client is the v1 path; native clients possible later, not now |
| Engine is pure C#, no framework references, headless-tested |
| Server authoritative; clients get filtered projections; hidden information from the start |
| Zones are general containers (piles, grids with tokens, counters), not card lists |
| Games are C# classes in the repo; no rules DSL until pain justifies one |
| No scripting layer in v1 |
| "Customisable" v1 = house-rule toggles via a setup config object |
| Custom card decks in v1 (data plus served images), gitignored per clone |
| In-app card editor kept: tier-1, data-driven, off the critical path |
| Room codes and env-var config, nothing hardcoded — one binary runs on a laptop or a public box |
| Rendering per game and pluggable against the event stream |
| Second game is the card-driven board game, to stress the zone model |
| Typed event vocabulary and query indirection from day one |
| Framework extracted from two or three real games, never designed up front |
| SignalR for transport; rooms, sessions, acks and projection stay hand-built |

## 10. Open questions

- **Public deployment.** The founding scene is "out together", where no laptop is hosting a LAN, so public reachability is load-bearing rather than a footnote. What is undecided is timing, and whether it lands on a homelab or a small VPS. The code is already deployment-neutral: room codes instead of "type my IP", binding from configuration, no hardcoded host.
- **Event sourcing.** State is currently a mutable `GameState` alongside an append-only event log. Making state a fold over events would give replay and mid-game rejoin almost for free, since actions already *are* events. The cost is a migration and a hot path that rebuilds rather than mutates.
- **Persistence.** Matches are ephemeral today, which suits a game played in one sitting. A database earns its place when standings should survive a restart.
- **More expressive cards.** Triggers, buffs and resources — the direction a collectible-card game implies — are deferred, not dropped. It is the path that would eventually justify data-driven effects, and the honest ceiling is worth stating: interactions at the scale of a stack-and-layers system are expressible but not free, and must never be built speculatively.

## 11. Known debt

The README's ["What I would change first"](../README.md#what-i-would-change-first) lists this in detail. In short: game-specific types still leak upward into `Protocol` and `Server` (accepted, pending the second game that shows the right seam); zone visibility is fixed at creation, so a showdown would have to move cards rather than reveal them in place; `GameState` models one match, so nothing spans a rematch; and the browser client has no committed automated test, while the server beneath it is covered thoroughly.
