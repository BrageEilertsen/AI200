# Azure Certification Trainer

A practice-exam app for Microsoft Azure certifications. Pick an exam, drill the areas you keep
getting wrong, sit a full mock against the clock, and read a cram sheet on the last day.

**[Open the app →](https://ai200-gch4fba7dsc4hkc5.canadacentral-01.azurewebsites.net)**

Two exams ship with it:

| Exam | Certification | Questions | Weighting |
| --- | --- | --- | --- |
| **AI-200** — Developing AI Cloud Solutions on Azure | Azure AI Cloud Developer Associate | 254 | 4 skill areas |
| **AZ-400** — Designing and Implementing Microsoft DevOps Solutions | DevOps Engineer Expert | 82 | 5 skill areas |

Switch between them from the control at the top of the sidebar. Each exam keeps its own
progress, weak-topic list, mock history and exam date — studying two certifications at once
never blends their numbers.

> **AI-200 is not AI-102.** It is the exam that replaces AZ-204, and it is a *developer* paper:
> containers, Cosmos DB, PostgreSQL with pgvector, Managed Redis, Service Bus, Event Grid,
> Functions, Key Vault, App Configuration, OpenTelemetry and KQL. There is very little Azure AI
> Services or prompt-engineering content, which is what makes most "AI-200" material online
> unusable — it is AI-102 content under a new name.

## How to study with it

| Screen | What it does |
| --- | --- |
| **Dashboard** | Bank size, how much you have covered, lifetime accuracy, last mock score, and a per-area breakdown against the published weightings |
| **Practice** | Choose skill areas and set size. Immediate feedback and a full explanation after every question. *Focus weak areas* favours what you have never seen or recently missed |
| **Exam simulation** | Questions drawn in the real weightings, a clock, flags and a question navigator. Nothing is graded until you submit |
| **Question bank** | Grouped by the certification's published **course syllabus**, so you can work through one learning path at a time and jump to it on Microsoft Learn. Plus full-text search across stems, options, explanations and tags |
| **Cram sheet** | The defaults, limits and distinctions each exam leans on. The highest yield per minute on the last day |
| **Progress** | Accuracy per objective and per topic tag, mock-score trend, and a ranked list of what to fix |

Set your exam date in the sidebar for a countdown. **Light / Dark / Auto** sits next to it and is
applied before first paint, so there is no flash of the wrong theme on reload.

Everything works on a phone: the sidebar becomes a top bar plus a bottom tab bar, and wide
tables and code blocks scroll on their own rather than pushing the page sideways.

### Keyboard

| Key | Practice | Exam simulation |
| --- | --- | --- |
| `1`–`9` / `A`–`E` | select option | select option |
| `Enter` | check, then next | next question |
| `←` `→` | — | previous / next |
| `F` | — | flag for review |

### How questions are graded

| Kind | Answer | Notes |
| --- | --- | --- |
| `single` | one option | the common case |
| `multi` | two or more | the stem says how many |
| `ordering` | every option, in sequence | order-sensitive |
| `blanks` | one choice per blank | dropdown completion, or a yes/no series |

**Answer positions are randomised every time a question is drawn.** This is not cosmetic. The
bank was authored with the correct answer written first, which left it at position A for 92% of
single-answer questions — you could have scored 92% by always picking A. Options are permuted,
relabelled A/B/C…, and the correct answers and per-distractor notes remapped to match.

The scaled score is a linear estimate where 70% maps to 700. Microsoft does not publish its
scaling curve, so treat anything near the line as *not safe yet*.

## Where the questions come from

Every question is written from the published skills outline and Microsoft Learn documentation,
and carries a link to the page it came from. Weightings, objective names and pass marks are
taken from the official study guides ([AI-200][sg-ai200], [AZ-400][sg-az400]).

They are **not** real exam items, and no claim is made about overlap with the live exam. If you
want questions in Microsoft's own style, sit the free official practice assessment linked from
each study guide — it is written by the same team.

[sg-ai200]: https://learn.microsoft.com/credentials/certifications/resources/study-guides/ai-200
[sg-az400]: https://learn.microsoft.com/credentials/certifications/resources/study-guides/az-400

## Your progress stays in your browser

Progress lives in `localStorage`, keyed by exam, and never reaches the server.

That is deliberate, because the app is deployed to a public URL. A server-side store would be
**one record shared by everyone who opens the site** — any visitor's answers would move your
accuracy, your weak-topic list and your mock history, and your study activity would be visible
to them. Browser storage gives each person their own history with no accounts and no database.

The trade-offs: progress does not follow you between browsers or machines, and clearing site
data wipes it. A full 50-question mock and its answers costs about 5 KB, so you will not run out
of room. **Reset progress** on the Progress screen clears history for the current exam only, and
keeps your exam date.

## Hands-on lab

[`labs/`](labs/README.md) is a runnable **pgvector** lab covering the heaviest part of AI-200
(25–30%). It brings up Postgres with pgvector in Docker and walks the index behaviour the exam
tests: IVFFlat built on an empty table, the `probes` recall dial, HNSW, operator-class mismatches
silently falling back to `Seq Scan`, and filtered ANN search under-returning until you add a
partial index. Every number quoted in it was measured against a running database, not estimated.

---

## Running it locally

Requires the .NET 10 SDK.

```bash
dotnet run
```

Then open <http://localhost:5120>.

<details>
<summary>If <code>dotnet run</code> fails with "Ingen tilgang" / "Access denied"</summary>

The csproj sets `<UseAppHost>false</UseAppHost>`. Some managed Windows machines have **AppLocker
rules** that block launching unsigned executables from the user profile, and .NET normally builds
a small native launcher (`ai-200-trainer.exe`) next to the DLL that `dotnet run` starts:

```
An error occurred trying to start process '…\bin\Debug\net10.0\ai-200-trainer.exe'
with working directory '…'. Ingen tilgang.
```

Suppressing the launcher makes `dotnet run` execute the DLL through `dotnet.exe` in
`C:\Program Files\dotnet\` — a signed binary in an allowed path. Remove the property if you need
a standalone `.exe`, or if the machine has no such policy.
</details>

## Adding an exam

An exam is a folder under `Data/`. Adding a certification is a data change, not a code change —
drop in a folder and restart.

```
Data/
  az-400/
    exam.json            manifest: code, title, domains, weightings, mock sizes, pass mark
    01-pipelines.json    questions, any number of files
    02-…
    case-studies.json    optional shared scenarios
    cram.md              optional cram sheet
```

`exam.json` declares the skill areas and their published weightings; mocks are allocated
proportionally across them. It can also carry `learningPaths`, the certification's published
course syllabus — when present, the question bank groups by learning path instead of by skill
area, and every question must then declare which path it belongs to. Question files are plain
arrays:

```jsonc
{
  "id": "p-yaml-001",             // unique within the exam
  "domain": "pipelines",          // must match a domain key in exam.json
  "path": "container-hosting",    // learning path key; required once exam.json has a syllabus
  "objective": "Design and implement pipelines",   // sub-heading from the skills outline
  "kind": "single",               // single | multi | ordering | blanks
  "difficulty": 2,                // 1 recall, 2 applied, 3 hard
  "stem": "…",
  "code": "optional CLI/YAML/KQL block shown above the options",
  "options": [{ "id": "A", "text": "…" }],
  "correct": ["B"],               // option ids; a sequence for `ordering`
  "explanation": "why the right answer is right",
  "whyWrong": { "A": "…" },       // optional, per distractor
  "docs": [{ "title": "…", "url": "https://learn.microsoft.com/…" }],
  "tags": ["azure-pipelines", "yaml"]   // drive the weak-topic list on Progress
}
```

Write options in whatever order reads best — presentation order is randomised anyway.

The loader validates on every load and reports problems on the dashboard: duplicate ids, unknown
domains, answers that are not options, single-answer questions with two correct answers, missing
explanations, malformed blanks. Edit the JSON and hit **Reload bank** — no rebuild needed when
running under `dotnet run`.

Wrap code in `` `backticks` `` anywhere in a stem, option, explanation, distractor note, blank
label or case-study body and it renders as a code span; everything outside the backticks is
HTML-encoded, so text can safely contain `<=>`, `<->` and `<#>`. Use plain prose otherwise —
`**bold**` and `*italics*` are not rendered in question text and will show as literal asterisks.

Cram sheets use a small markdown subset: `## Section | weight`, `### Sub-heading`, `- fact`,
`**bold**` and `` `code` ``.

## Architecture

Blazor Server on .NET 10. No database, no accounts, no external services.

| Piece | Role |
| --- | --- |
| `Services/ExamCatalog.cs` | Loads and validates every exam under `Data/` at startup. Singleton |
| `Services/ExamContext.cs` | The visitor's current exam, and the weighted draw for practice sets and mocks. Scoped to the circuit |
| `Services/ProgressStore.cs` | Per-exam history in `localStorage`, keyed by exam slug |
| `Services/SessionHost.cs` | The in-flight practice set or mock |
| `CramSheet.cs` | Renders the markdown subset used by cram sheets |

Pages holding state derived from the bank — selected skill areas, filters, an open set or mock —
subscribe to `ExamContext.Changed` and reset when the exam is switched, so one certification's
questions can never end up on another's clock.

<details>
<summary>Two styling rules that are easy to reintroduce as bugs</summary>

**Numbers in CSS and SVG must be culture-invariant.** Razor interpolates using the current
culture, so on a Norwegian machine `width: 52.3%` renders as `width: 52,3%` and a circle radius
of `27.5` renders as `27,5`. Browsers reject both silently, and every meter and progress ring
disappears. Anything destined for a `style` or SVG attribute goes through [`Css.cs`](Css.cs)
(`Css.Pct`, `Css.Px`, `Css.Num`). Display text still uses the local culture, which is why the
countdown reads *tirsdag 18. august*.

**Themed colours use `background-color`, never the `background` shorthand.** Chromium does not
reliably invalidate the shorthand when a `var()` it references is redefined by flipping
`data-theme` on `:root` — the element keeps painting the old colour, and elements with a
`transition` on that property never repaint at all. Hence `background-color: var(--x)`,
`transition: background-color …`, and the `.theme-switching` class that suppresses transitions
for the single frame in which the palette swaps.
</details>

## Deploying

`.github/workflows/azure-webapp.yml` publishes to Azure App Service on every push to `main`.
Two settings the app needs that are not in the repo:

- **Enable WebSockets** (App Service → Configuration → General settings). Off by default, and
  without it Blazor Server falls back to long polling — functional, but every click feels
  sluggish. Leave ARR affinity **on**; a Blazor circuit is bound to one instance.
- **HTTPS Only** on. `UseHttpsRedirection()` is deliberately absent so local HTTP development
  works; TLS is terminated by the platform.

Question banks and cram sheets are copied into the publish output, so a deployed instance loads
every exam with no extra configuration.

---

Independent study aid built from the published skills outlines. Not affiliated with, endorsed by,
or connected to Microsoft.
