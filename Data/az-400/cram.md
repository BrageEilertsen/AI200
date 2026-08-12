## Pipelines | 50–55%

### YAML pipeline anatomy

- Hierarchy is **stages → jobs → steps**. A job runs on one agent; a stage is a boundary where approvals and checks apply.
- `trigger:` is the CI trigger for pushes. `pr:` is the pull-request trigger and **only applies to GitHub and Bitbucket** — in Azure Repos, PR builds come from a **branch policy**, not from `pr:`.
- `trigger: none` disables CI; `pr: none` disables PR validation. `schedules:` uses cron and by default **only runs when the branch has changed** unless you set `always: true`.
- `dependsOn` orders jobs and stages; without it, stages run in sequence and jobs in parallel. GitHub Actions uses **`needs`** for the same thing — a classic mix-up.
- `condition:` controls whether something runs. Once you set a custom condition you lose the implicit "previous succeeded", so the common shape is `and(succeeded(), <your test>)`.
- Variable syntax, and this is examinable: `$(var)` is expanded at **runtime** by the agent, `${{ }}` is **compile-time** template expansion, and `$[ ]` is **runtime expression** evaluation used in `variables` and `condition` blocks.
- **Templates** are the reuse unit: step, job, stage or variable templates, with typed `parameters`. They can live in another repository declared under `resources: repositories:`.
- `extends:` means the pipeline is built from a template. This is what a **template check** on a protected resource can require — the mechanism that stops a team removing a mandatory security scan.
- **`resources: pipelines:`** declares a dependency on *another* pipeline: it can trigger on that pipeline's completion and makes its artifacts downloadable. `dependsOn` cannot cross pipelines.
- **`strategy: matrix`** expands one job into parallel copies with different variable values (three .NET versions). **`strategy: parallel`** slices the *same* work across agents, which is for distributing tests. Do not confuse them.
- **Task groups are classic-only.** In YAML the equivalent is a template.

### Approvals, checks and environments

- **Approvals and checks live on the resource, not the pipeline.** Checks can be configured on **environments, service connections, repositories, variable groups, secure files and agent pools**. Microsoft is blunt about why: checks "aren't defined in the yaml file", so whoever edits the pipeline cannot grant themselves a bypass.
- Available checks: **Branch control** (resources must come from allowed, protected branches), **Required template** (the run fails unless the pipeline extends the named template), **Evaluate artifact** (custom policy — *container images only*), Approval, Business hours, Invoke Azure Function, Invoke REST API, Query Azure Monitor alerts, Exclusive lock, ServiceNow change management.
- Evaluation order matters if a question asks: **static checks** (branch control, required template, evaluate artifact) → pre-check approvals → **dynamic checks** (approval, function, REST, business hours, Monitor alerts) → post-check approvals → **exclusive lock**.
- **Exclusive lock** `lockBehavior`: `runLatest` (default — only the newest run takes the lock) or `sequential` (every run takes it in turn).
- An **environment** also gives you deployment history per resource and is what `deployment` jobs target.
- `deployment` jobs support strategies **`runOnce`**, **`rolling`** and **`canary`**, each with `preDeploy`, `deploy`, `routeTraffic`, `postRouteTraffic` and `on: success | failure` hooks.
- In **GitHub Actions**, the equivalent is a **GitHub environment** with required reviewers, a wait timer and deployment branch restrictions.

### Deployment strategies

- **Blue-green:** two identical environments, flip 100% of traffic at once. Instant rollback because the old version is still running. App Service **slot swap** is the canonical implementation.
- **Canary:** expose a small percentage, watch health signals, widen. Needs traffic-splitting — Container Apps multiple-revision mode, Traffic Manager/Front Door weights, or a service mesh.
- **Rings:** the same idea expressed as named cohorts — staff, then pilot customers, then everyone — with a bake time at each ring. Canary and rings are both **progressive exposure**.
- **A/B testing is not a release strategy.** Canary and rings ask *is this build safe*; A/B asks *which variant performs better*. Same plumbing, different question — the exam tests that distinction.
- **Rolling:** replace instances a few at a time. Kubernetes does this by default and **stalls with the old ReplicaSet still serving** if new pods fail readiness — that is your automatic safety net; `kubectl rollout undo` completes the rollback.
- **Feature flags** separate deploy from release. Ship dark, flip the flag at run time, kill instantly if it misbehaves. Azure App Configuration is the managed service.
- **Database changes use expand/contract:** add the backward-compatible shape, deploy code that works with old *and* new, then remove the old shape in a **later** release. Every intermediate state stays rollback-able.
- **Build once, deploy many.** The build stage publishes a pipeline artifact; every environment deploys the same bytes. If you rebuild per environment you cannot claim test and production are the same.

### Infrastructure as code

- **Idempotent** = the template describes desired state, so applying it once or ten times converges on the same result. This is what makes re-running after a partial failure safe.
- **Deployment modes:** `Incremental` (default) leaves resources not in the template alone; `Complete` **deletes** them. Complete mode on a shared resource group is how teams destroy each other's resources.
- `az deployment group what-if` previews adds, changes and deletes against live state. `validate` only checks the template is deployable and reports **no diff**.
- **Bicep** transpiles to ARM JSON. Modules are the reuse unit; publish them to a registry (an ACR) or use **Template Specs** to share versioned templates in a subscription.
- **Terraform state** must be remote and locked — an Azure Storage blob backend uses blob leases. Agents are ephemeral and runs overlap, so local state is not an option.
- **Azure Machine Configuration** (formerly Policy guest configuration) audits and remediates settings **inside** the VM guest OS, continuously. ARM/Bicep only describes the resource.
- Configuration management tools: Ansible (agentless, push), Chef/Puppet (agent, pull), DSC.
- **Azure Deployment Environments:** developers self-serve environments from a **catalog** of environment definitions held in a Git repository, deployed under the platform's identity — so they get environments without standing subscription access.

### Packages and artifacts

- **Pipeline artifacts** move build output between stages and are the modern replacement for build artifacts. **Azure Artifacts feeds** hold versioned packages for consumption.
- **Upstream sources** turn a feed into a proxy of nuget.org, npmjs, PyPI or Maven Central, caching a copy in your feed. One URL for builds, and protection against a public package being unpublished.
- **Views** (`@Local`, `@Prerelease`, `@Release`) are filtered slices. **Promoting** a version to a view publishes it to that audience without republishing the artifact.
- **SemVer:** major = breaking, minor = backward-compatible addition, patch = backward-compatible fix. Removing a public member is **major**.
- **CalVer** (`2026.08.3`) encodes *when*, not *whether it breaks you*. Fine for products on a release cadence; SemVer for libraries other people compile against.
- **Retention** on feeds and pipelines controls what is kept. Package retention counts *versions*, not age, by default. Pipeline retention expires runs and their artifacts; **retain a run (a lease)** to keep anything you actually released.
- **Image tags:** use an immutable unique tag (build id or commit SHA) for traceability, and `latest` only as a convenience pointer.

### Testing and pipeline health

- Test pyramid maps onto stages: many fast **unit** tests on every commit, fewer **integration** tests against real dependencies, slow **load** and **UI** tests before production or on a schedule.
- **Quality gates** are automated pass/fail on an objective signal — coverage threshold, no critical vulnerabilities, no active Azure Monitor alerts. Automated beats "someone will notice".
- **Code coverage on pull requests is advisory by default** — it posts a status check but does *not* block the merge. It becomes a gate only when a **branch policy** is configured against that status check. The threshold lives in **`azurepipelines-coverage.yml`** at the repo root (not in the pipeline YAML, so it applies whichever pipeline builds the code); **diff coverage** — coverage of the changed lines only — defaults to **70%**.
- **Flaky tests** destroy trust in the gate. Azure Pipelines can detect and mark them so they stop blocking, which buys time to fix the nondeterminism. Blind retries hide real intermittent bugs.
- Speed up builds: **pipeline caching** keyed on the lock file (the fix for restore-dominated builds), test **parallelisation** across agents, shallow clone, and job-level parallelism.
- Health trio to watch: **failure rate, duration, flaky test count.** All three are on the pipeline analytics report.
- A failure that follows **one agent** is an agent problem, not a pipeline problem — build agents from an image or run them as containers so they cannot drift.
- Jobs queue while other agents sit idle → the pipeline's **demands** do not match any agent's **capabilities** in the pool it targets. Pools are the allocation unit.

### Agents

- **Microsoft-hosted:** clean VM per job, maintained by Microsoft, no access to private networks. **Self-hosted:** yours to patch, but can reach private endpoints, keep a warm cache, and carry custom tooling.
- Reaching a private database is the textbook reason for self-hosted. Allow-listing Microsoft-hosted IP ranges effectively exposes the resource to all of Azure Pipelines.
- **Scale set agents** give you self-hosted agents with elastic capacity and a fresh image per job.

## Source control | 10–15%

### Branching

- **Trunk-based development:** short-lived branches, merged to `main` at least daily, `main` always releasable. Paired with feature flags for unfinished work. This is what CD assumes.
- **GitHub Flow:** one always-deployable `main`, short-lived branches, everything merged back through a pull request. Deliberately minimal — no `develop`, no release branches.
- **GitFlow** has long-lived `develop`, `release/*` and `hotfix/*` branches — deliberately delayed integration, appropriate for versioned/boxed products, rarely the right answer for continuous delivery.
- **Release branch** for a hotfix: branch **from the release tag**, fix, ship, then **merge back** into main. Forgetting the merge back is how the bug reappears next release.
- **Environment branches** (a long-lived branch per environment) cause drift and are an anti-pattern; promote the same artifact instead.

### Branch policies and pull requests

- Policies available: minimum reviewers, **check for linked work items**, comment resolution, **build validation**, status checks, **automatically included reviewers** scoped to paths, and merge-type restrictions.
- Build validation set to **required** blocks completion; set to **optional** it runs but does not block — the gentle way to introduce a new check.
- **Merge types:** merge (no fast-forward) keeps every commit plus a merge commit; **squash** collapses to one commit; **rebase** replays each commit linearly; **semi-linear** rebases then adds a merge commit.
- GitHub's path-based review routing is **`CODEOWNERS`**, made binding by "require review from Code Owners" in branch protection.

### Repository management

- Large binaries → **Git LFS**: a pointer in the repository, content in separate storage. Git otherwise stores a full new object per version and history grows without bound.
- **Scalar** is the answer to a different problem: repository *scale* — very many files, very deep history. It configures partial clone, sparse-checkout and background maintenance. LFS = a few huge files; Scalar = a huge number of them.
- **Committed secret:** rotate the credential **first** — anyone who cloned still has it — then purge from history and force-push. Purging alone is not remediation.
- Splitting a monolith but sharing code → publish the shared library as a **versioned package**. Submodules pin a commit rather than a version and go stale.
- `.gitignore` (what not to track), `.gitattributes` (line endings, diff drivers, LFS tracking), `CODEOWNERS` (review routing), `.github/` (templates, workflows).
- Monorepo vs multi-repo: monorepo gives atomic cross-component changes and one version of the truth; multi-repo gives independent release cadence and clearer ownership.

## Process and flow | 10–15%

### Traceability

- The chain is **work item → commit → pull request → build → release**. Once the links exist, "what shipped last night?" is answerable by the tooling rather than by bookkeeping.
- The **check for linked work items** branch policy is the enforcement point.
- `#123` in a commit message or PR description links to work item 123; `Fixes #123` on a GitHub PR closes the issue on merge.

### Metrics

- **DORA four:** *deployment frequency* and *lead time for changes* measure throughput; *change failure rate* and *mean time to restore* measure stability. They are deliberately paired — optimise one pair alone and you get fast-and-broken or safe-and-slow.
- **Lead time for changes** = commit to running in production. Do not confuse it with **cycle time** (work started to done) or **lead time** on a board (created to done).
- **Cumulative flow diagram:** a widening band means work in progress is accumulating; the horizontal width of a band is cycle time. The response is a **WIP limit** — finish before you start.
- **Analytics service** is the reporting layer: keeps historical snapshots (so trends are possible), exposes **OData** for Power BI, and backs the built-in widgets. Work item **queries** show current state only.
- Watch for vanity metrics — lines of code, commit counts, story points delivered. They measure activity, not outcome.

### Culture and communication

- **Blameless postmortem:** the question is what systemic conditions allowed the failure, never who did it. People withhold exactly the information you need when they expect blame.
- Notifications should be a by-product of the event: **service hooks** and the Teams/Slack apps push build, release and work item events as they happen.
- Documentation lives with the code — README, CONTRIBUTING, and an Azure DevOps **wiki published from a repository folder** so it is versioned and reviewed like everything else.
- Wikis render **Mermaid** inside a `::: mermaid` block, so architecture diagrams are plain text that diffs in a pull request rather than a pasted image that drifts.
- **Release notes from Git history** only work if the history is structured — a conventional commit format (`feat:`, `fix:`) lets a tool group changes and even derive the next SemVer bump.

## Security and compliance | 10–15%

### Authentication and authorization

- **Workload identity federation** is the current best answer for pipeline-to-Azure auth: the pipeline presents a signed **OIDC** token, Entra ID validates it against a federated credential and issues a short-lived token. **No stored secret to leak or expire.**
- Ranking for service connections: workload identity federation → managed identity → service principal with certificate → service principal with secret → PAT. A PAT ties deployments to a person.
- **Managed identity** for a running Azure resource that needs to call another Azure service. System-assigned dies with the resource; user-assigned is shared and survives.
- **Service connection scope:** turn off "grant access to all pipelines" and authorise specific pipelines. Otherwise anyone who can create a pipeline in the project can borrow production credentials.
- Least privilege on **scope** and **time** is what shrinks blast radius: narrow the resource scope, shorten the credential lifetime.
- **GitHub credentials, in order of preference:** `GITHUB_TOKEN` (minted per run, scoped to the repository, expires with the job — narrow it further with a `permissions:` block) → **GitHub App** installation token (short-lived, spans repositories, not tied to a person) → **PAT** (long-lived, personal, breaks when they leave). Deploy keys are Git access only and cannot call the API.

### Secrets

- **Key Vault-backed variable group** so the pipeline reads the current value at run time. Rotation takes effect immediately and the vault logs every read. Copying values into pipeline variables goes stale instantly.
- Secret variables are **masked** in logs on a best-effort basis. Transform the value — base64, split, reverse — and the mask no longer matches, so it prints in full. Do not echo secrets.
- Secret variables are **not** automatically available as environment variables in scripts; you map them explicitly with an `env:` block.
- **Push protection** blocks the push when a known secret pattern is detected — the only control that acts *before* the secret enters history. Secret scanning alerts afterwards, at which point rotation is mandatory.
- **Secure files** are for *binaries* a build needs but must never be committed — signing certificates, keystores, provisioning profiles. The Download Secure File task puts one on the agent for the run and removes it afterwards.

### Scanning and governance

- **SAST** (CodeQL code scanning) analyses **your** code for flaws like injection. **SCA / dependency scanning** (Dependabot) checks **third-party packages** against vulnerability databases. **DAST** probes a running app. **Secret scanning** finds credentials.
- **GitHub Advanced Security for Azure DevOps** brings code scanning, dependency scanning and secret scanning to Azure Repos.
- **Azure Policy** evaluates at the resource provider, so a **deny** effect blocks the request whatever path it came from — portal, CLI, SDK or pipeline. A pipeline check only covers deployments through that pipeline.
- Policy effects worth knowing: `Deny`, `Audit`, `Append`, `Modify`, `DeployIfNotExists`, `AuditIfNotExists`. `DeployIfNotExists` and `Modify` need a managed identity for remediation.
- **Defender for Cloud** assesses deployed resources and gives a secure score; it reports rather than blocks. Its **DevOps security** connects Azure DevOps and GitHub organisations so code findings sit next to cloud posture — GHAzDO *produces* the findings, Defender for Cloud *aggregates* them across organisations.
- **SBOM** — a software bill of materials — lists every component in a build, which is what makes "are we affected by this CVE?" answerable in minutes.

## Instrumentation | 5–10%

### Azure Monitor

- **Metrics** are pre-aggregated numeric time series: cheap, low-latency, ideal for alerting. **Logs** live in a **Log Analytics workspace** and are queried with **KQL**: expressive, scheduled evaluation, higher cost.
- Alert on a fast-moving numeric threshold → **metric alert**. Alert on something requiring a query or correlation → **log search alert**. Control-plane events like a resource deletion → **activity log alert**.
- **Diagnostic settings** are the pipe that routes resource logs and metrics into a workspace, storage or Event Hubs. The workspace is the destination, not the mechanism.
- **Action groups** define what happens when an alert fires: email, SMS, webhook, Logic App, Azure Function, ITSM.
- **The Insights family splits by layer:** Container Insights for AKS nodes and pods, VM Insights for machine CPU/memory plus the **dependency map**, Application Insights for what happens inside the code. Questions turn on which layer the symptom lives at.

### Application Insights

- **Distributed tracing** correlates telemetry across services by **operation id** propagated in request headers; the **end-to-end transaction view** reconstructs the call chain with timings.
- **Adaptive sampling** cuts ingestion cost while keeping aggregate counts statistically correct — the sampling rate is recorded and counts scaled back up, and related items sample together so traces stay coherent.
- **Live Metrics** is the near-real-time stream for watching a deployment go out. **Availability tests** probe endpoints from outside on a schedule.
- Useful tables: `requests`, `dependencies`, `exceptions`, `traces`, `customEvents`, `pageViews`.
- KQL shape to know cold: `requests | where timestamp > ago(1h) and success == false | summarize count() by name | order by count_ desc`. An unnamed `count()` produces the column **`count_`**.

### SRE concepts

- **SLI** is the measurement, **SLO** is the internal target, **SLA** is the contractual promise with consequences. SLO should be stricter than SLA.
- **Error budget** = 1 − SLO. 99.9% availability leaves 0.1% to spend on risk; exhausting it is the agreed trigger for prioritising stability over features.
- **Toil** is manual, repetitive, automatable work that scales with service size. Reducing it is the point of the discipline.
- Alert on **symptoms users feel** (latency, error rate, saturation), not on every internal cause — that is what keeps on-call sustainable.
