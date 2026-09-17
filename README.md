# tabletop-core

A platform for playing custom card games with friends **in person, on whatever phones they own** — no install, no app store, mixed iPhone and Android, one URL on the local network.

The first game is **Agony**, a shedding game in the UNO family with configurable house rules. It exists to prove the engine, not the other way round: the rules engine underneath is a standalone C# library that knows nothing about Agony, ASP.NET, or any client.

**Status:** playable end to end. Two players on two phones can create a room, argue about house rules, and play a full game over LAN, with reconnection that survives a screen lock. 135 tests, all green.

```
dotnet test                                                   # 135 tests
dotnet run --project src/Server --urls http://0.0.0.0:5199    # then open http://<your-lan-ip>:5199

# or play it in terminals, one per player
dotnet run --project clients/console -- create Ava
dotnet run --project clients/console -- join ABCD Bo --server http://localhost:5199
```

For why the project is shaped this way — the constraint that picked the web, the layering rules, hidden information, and the open questions — see [docs/DESIGN.md](docs/DESIGN.md).

---

## Architecture

```
clients/console ─┐                         ┌─ src/Engine     pure C#, zero framework refs
                 ├─► src/Server (SignalR) ─┤
wwwroot (phones)─┘         │               └─ src/Games      one class per game
                           │
                     src/Protocol           the wire contract: JSON, converters, payloads
```

Four rules hold the shape together:

1. **The engine references nothing.** No ASP.NET, no Unity, no JSON. It is a headless library exercised entirely by xUnit, and the compiler enforces it through project references. The same assembly could run inside a Unity client later without a rewrite.
2. **The server is authoritative and clients are dumb.** A client never receives `GameState` — it receives a `PlayerView` projection (own hand in full, everyone else's as counts) plus the list of moves it is allowed to make. Clients render buttons; they never evaluate a rule.
3. **Games drive the engine; the engine never calls games.** A game composes engine *actions* (`MoveCard`, `Draw`, `Shuffle`, `SetCounter`) whose setters are `internal` to the engine, so every rule automatically produces a correct event trail.
4. **Cards are inert data.** A `CardDefinition` is a name, an optional face-image URL, and a property bag. Behaviour lives in the game class, never on the card.

## Design decisions worth defending

**Hidden information was built in at commit two, not bolted on.** Retrofitting it means rewriting every message the server sends. `StateProjector` filters state per viewer from zone visibility (`Public` / `OwnerOnly` / `Hidden`), and `EventProjector` does the same for the event stream — others learn *that* you drew, never *what* (a `CardsDrawnHidden` event replaces `CardDrawn`). A test sweep plays whole games and asserts the serialized bytes never name a card the viewer cannot see.

**Card instance ids are seed-randomized.** Instances used to be created in generation order, so "card #0" was always Red 0 — and this source is public. Creation order is now drawn from the seeded RNG, making ids meaningless across games while staying replay-deterministic.

**No behavioural tells.** Hidden information can leak through what the *system* does, not just what it sends. There is no auto-pass when you have nothing playable (firing instantly would signal "has nothing"; not firing would signal "saving something"), and no hand-private data lives on public zones.

**SignalR, after a spike that argued the other way.** Raw `System.Net.WebSockets` was the original decision. A throwaway spike hand-rolled that layer — framing, per-socket send gates, reconnect, keepalive — measured it, and concluded those parts are undifferentiated plumbing whose bugs are concurrency bugs. What SignalR does *not* provide stayed hand-built, because it is the part that matters: rooms, sessions, acks, projection.

**A room is an actor.** Every request becomes a command on a channel consumed by a single task that owns the `GameState`. Game logic is therefore single-threaded with no locks, moves have a total order, and broadcasts cannot interleave. Time-driven work (the disconnect sweep) posts a command through the same inbox rather than touching state on a timer thread.

**Session ≠ connection.** SignalR reconnecting hands you a *new connection id* — it restores the pipe, never the seat. Seats are session tokens that outlive connections; `resume(token)` reattaches. Moves carry client-chosen ids and the server dedupes, so a move in flight when the connection dies is resent and applied **exactly once** — verified by an integration test that kills a socket between the ack and its delivery.

**Determinism is a contract.** A hand-rolled PCG32 (not `System.Random`, whose algorithm is not stable across .NET versions) means the same seed plus the same actions always reproduces the same game.

---

## Extension points — what a second game plugs into

The engine was built to be extracted *from* real games rather than designed up front, so these are the seams that already exist. A second game touches only `src/Games`.

| Seam | What it is |
|---|---|
| `IGame` | The whole contract: `Setup`, `TryMove`, `GetLegalMoves`, `GetStandings`, `IsFinished`. Five methods and a name. |
| `GameMove` | Abstract record; a game subclasses it with its own vocabulary (`PlayCard`, `Bet`, `MoveToken`). **The actor is deliberately not part of a move** — the server derives it from the connection, so a client cannot act as another player. |
| `GameEvent` | The engine's typed event vocabulary, emitted by actions. Renderers animate it; the projector redacts it. |
| `Zone` | A general container — ordered cards plus named counters — not `List<Card>` per pile. A deck, a hand, a discard, a pot, a board square are all zones with different `Kind`/`Visibility`. |
| Table counters | Where public game state lives (active colour, pending draw, finishing order). Ints keyed by string, on a zone. |
| `ITurnSystem` | "Who acts next?" is pluggable. `RoundRobinTurnSystem` ships in the engine; Agony swaps in `AgonyTurnSystem`, which steps over players who have gone out. |
| `state.GetValue(card, "…")` | All property reads go through one query indirection, so a future modifier/buff layer intercepts a single method instead of every rule. |
| `CardCatalog` | Definition id → name, properties, `faceImage` URL. Sent once per game; already the shape a `cards.json` deck will feed. |

**A worked example.** Adding Texas Hold'em would need: a `HoldemMove` hierarchy (`Fold`/`Call`/`Raise`); zones for hole cards (`OwnerOnly`), community cards (`Public`, revealed with the existing `FlipCardAction`), and a pot (counters); an `ITurnSystem` implementing betting-round order, where a raise reopens action; and `GetStandings` returning the showdown ranking. Hand evaluation is pure game logic and needs nothing from the engine.

## What I would change first

The honest list, in the order the pain would arrive.

**Games leak upward into `Protocol` and `Server`.** Six places name Agony concretely:

- `Protocol/Json/WireTypeResolver.cs` registers Agony's move types for polymorphic JSON — the base types live in `Engine`, which cannot see `Games`, so the registry has to sit above both.
- `Protocol/Payloads.cs` types `LobbyPayload.Config` as `AgonyConfig`; `Server/GameHub.cs`, `Rooms/RoomCommands.cs` and `Rooms/Room.cs` carry that through, and `Room` constructs `new AgonyGame(...)` directly.

This is **accepted debt, not an oversight** — the framework is meant to be extracted from two or three real games rather than guessed at. The fix is known: have each game contribute its own wire types and a discriminator prefix through an `IGame`-side registration hook, and make the lobby config an opaque per-game blob, so `Protocol` depends on an abstraction and `Room` resolves a game from a registry. The seam is already half-built: `RoomRegistry.Create` takes a `gameId` and validates it against `"agony"`, which is exactly where a factory lookup belongs.

**Zone visibility is fixed at creation.** There is no `RevealedTo` and no way to change visibility mid-game, so a poker showdown would have to move cards into a public zone rather than reveal them in place.

**`GameState` is one match.** Anything spanning hands — poker chips, running scores across a rematch — has nowhere to live, because a rematch builds a fresh `GameState`.

**`ZoneKind.Grid` is an enum member with nothing behind it,** deliberately deferred until a board game demands cell/token structure.

**`EffectEntry` is defined and unread** — the data-driven card hook that the planned in-app card editor would target.

**The browser client has no committed automated test.** It is verified by hand and by ad-hoc Playwright drives; the server beneath it is covered thoroughly, but the renderer is not.

---

## Testing

135 tests, no mocking framework, no test doubles for the domain — positions are engineered by applying the same public actions a game uses.

- **Engine** — action pipeline, rejection leaving zero trace, seeded shuffle determinism, replay equality, projection filtering.
- **Games** — Agony's rules, including determinism per seed and every house rule.
- **Protocol** — round trips, **golden JSON strings** that freeze the wire shape, and a leak sweep that plays full games asserting no payload names a card its viewer cannot see.
- **Server** — the room actor driven below the hub through a recording fake, plus full games over a real in-memory SignalR connection; disconnect grace and exactly-once move application run on an injectable `TimeProvider`, so "two minutes pass" is a method call.

## Repo layout

```
src/Engine        pure C# rules engine — zero package and framework references
src/Games         game classes (Agony), referencing only Engine's public surface
src/Protocol      the wire contract — the only assembly that knows JSON exists
src/Server        ASP.NET Core + SignalR; wwwroot/ is the browser client (no build step)
clients/console   over-the-wire terminal client, one per player
tests/            Engine, Games, Protocol, Server
content/          custom decks — gitignored from the first commit, local per clone
```

## License

MIT — see [LICENSE](LICENSE). The one vendored third-party file, the SignalR browser client, is MIT too; its notice is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

`content/` is gitignored deliberately and from day one: the code is meant to go public, the photos friends put on their custom cards are not, and that split has to be real before the repo is ever public so there is no history to scrub.
