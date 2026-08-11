# AI-200 Trainer

Offline study app for Microsoft exam **AI-200 — Developing AI Cloud Solutions on Azure**
(Azure AI Cloud Developer Associate, the exam that replaces AZ-204).

> AI-200 is **not** AI-102. It is a *developer* exam: containers, Cosmos DB, PostgreSQL +
> pgvector, Managed Redis, Service Bus, Event Grid, Functions, Key Vault, App Configuration,
> OpenTelemetry and KQL. There is very little Azure AI Services / prompt engineering content.

Blazor Server on .NET 10. No database, no external services, no accounts — it runs entirely
from a JSON question bank and a local progress file.

## Run it

From this folder (`dev\learning\ai-200-trainer`):

```bash
dotnet run
```

Or from the parent folder (`dev\learning`):

```bash
dotnet run --project ai-200-trainer
```

Then open <http://localhost:5120>. If the port is already in use, something else is still
listening — `Get-NetTCPConnection -LocalPort 5120 -State Listen` will tell you what.

### Why `<UseAppHost>false</UseAppHost>` is in the csproj

This machine has **AppLocker rules in effect**, which block launching unsigned executables from
the user profile. By default .NET builds a small native launcher (`ai-200-trainer.exe`) next to
the DLL and `dotnet run` starts *that*, which AppLocker refuses:

```
An error occurred trying to start process '…\bin\Debug\net10.0\ai-200-trainer.exe'
with working directory '…'. Ingen tilgang.
```

Setting `UseAppHost` to `false` stops the launcher being generated, so `dotnet run` executes the
DLL through `dotnet.exe` in `C:\Program Files\dotnet\` — a signed binary in an allowed path.

The equivalent one-off workaround, without the csproj change, is to run the DLL directly:

```bash
dotnet bin\Debug\net10.0\ai-200-trainer.dll
```

Remove the property if you ever need a standalone `.exe`, or if you move the project to a
machine without those policies.

## What's in it

| Screen | What it's for |
| --- | --- |
| **Dashboard** | Bank size, coverage, lifetime accuracy, last mock score, per-area breakdown |
| **Practice** | Pick areas and size, immediate feedback and explanation after every question |
| **Exam simulation** | 50 questions drawn in the published weightings, 110-minute clock, flags, navigator, nothing graded until you submit |
| **Question bank** | Full-text search across stems, options, explanations and tags |
| **Cram sheet** | Defaults, limits and the distinctions the exam leans on |
| **Progress** | Accuracy per objective and per topic tag, mock-score trend, weak-topic list |

Set your exam date in the sidebar for a countdown. The **Light / Dark / Auto** switch below it
persists to `localStorage` and is applied before first paint, so there is no flash of the wrong
theme on reload. *Auto* follows the OS.

### Keyboard

| Key | Practice | Exam |
| --- | --- | --- |
| `1`–`9` / `A`–`E` | select option | select option |
| `Enter` | check, then next | next question |
| `←` `→` | — | previous / next |
| `F` | — | flag for review |

## The question bank

136 questions in `Data/*.json`, one file per skill area, weighted roughly to the real exam:

| File | Area | Weight | Questions |
| --- | --- | --- | --- |
| `01-containers.json` | Develop containerized solutions | 20–25% | 33 |
| `02-data.json` | AI solutions with Azure data services | 25–30% | 42 |
| `03-integration.json` | Connect to and consume Azure services | 20–25% | 30 |
| `04-operations.json` | Secure, monitor, troubleshoot | 20–25% | 31 |

Edit the JSON and hit **Reload bank** on the dashboard — no rebuild needed when running via
`dotnet run`. The loader validates on every load and reports problems (duplicate ids, answers
that aren't options, single-answer questions with two correct answers, missing explanations)
in a banner on the dashboard.

### Question format

```jsonc
{
  "id": "c-acr-001",              // unique; prefix by area for readability
  "domain": "containers",         // containers | data | integration | operations
  "objective": "Implement container application hosting",  // sub-heading from the skills outline
  "kind": "single",               // single | multi
  "difficulty": 2,                // 1 recall, 2 applied, 3 hard
  "stem": "…",
  "code": "optional CLI/YAML/KQL block",
  "options": [{ "id": "A", "text": "…" }],
  "correct": ["B"],               // must match option ids
  "explanation": "why the right answer is right",
  "whyWrong": { "A": "…" },       // optional, per distractor
  "docs": [{ "title": "…", "url": "https://learn.microsoft.com/…" }],
  "tags": ["acr", "acr-tasks"]    // drive the weak-topic list on the Progress screen
}
```

## Progress

Stored at `%LOCALAPPDATA%\Ai200Trainer\progress.json` — outside the project, so it survives
rebuilds and `git clean`. It holds per-question attempt history (used by the *focus weak areas*
picker), finished session records, and your exam date. **Reset progress** on the Progress screen
clears history but keeps the exam date.

## Two non-obvious things in the styling

Both of these were real bugs that are easy to reintroduce.

**Numbers in CSS and SVG must be culture-invariant.** Razor interpolates with the current
culture, so on a Norwegian machine `width: 52.3%` renders as `width: 52,3%` and a circle radius
of `27.5` renders as `27,5` — browsers reject both silently, so every meter and progress ring
just disappears. Anything destined for a `style` attribute or an SVG attribute goes through
[`Css.cs`](Css.cs) (`Css.Pct`, `Css.Px`, `Css.Num`). Display text still uses the local culture,
which is why the countdown reads *tirsdag 18 august*.

**Themed colours use `background-color`, never the `background` shorthand.** Chromium does not
reliably invalidate the shorthand when a `var()` it references is redefined by flipping
`data-theme` on `:root` — the element keeps painting the old colour. Elements with a
`transition` on that property are worse: they never repaint at all. Hence `background-color:
var(--x)`, `transition: background-color …`, and the `.theme-switching` class that suppresses
transitions for the one frame in which the palette swaps.

## Caveats

- The scaled score is a linear estimate where 70% maps to 700. Microsoft does not publish its
  scaling curve, so treat anything near the line as "not safe yet".
- Questions are written from the published skills outline and Microsoft Learn documentation.
  They are not real exam items and no claim is made about overlap with the live exam.
- Not affiliated with or endorsed by Microsoft.
