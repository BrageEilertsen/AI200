## Containers | 20–25%

### Azure Container Registry

- **SKUs:** Basic, Standard, Premium. **Premium only:** geo-replication, private link with private endpoints, content trust, customer-managed keys, dedicated data endpoints, connected registries, IP access rules, retention policy for untagged manifests. If a question mentions any of those, the answer is Premium.
- **Not Premium-only, despite looking like it:** repository-scoped tokens and scope maps work on **all three tiers** (only the token count differs), and so do **availability zones** and webhooks. **Anonymous pull** needs Standard or Premium.
- `az acr build` runs the build **in Azure** — no local Docker daemon needed. This is the answer whenever a build agent has no Docker installed.
- `az acr import` copies an image registry-to-registry **without pulling it locally**. Faster and cheaper than pull + retag + push.
- **ACR Tasks trigger types:** quick task (manual), source-code commit, **base image update**, and schedule. A base-image trigger only fires if the task tracked the base image at build time.
- **Auth, best to worst:** managed identity → service principal → repository-scoped token → admin user. The admin account is disabled by default and is a single shared credential; never the right answer for production.
- `AcrPull` to pull, `AcrPush` to push. Attach a registry to AKS with `az aks update --attach-acr`, which assigns AcrPull to the kubelet identity.
- Multi-step tasks are defined in YAML (`acr-task.yaml`) with `build`, `push` and `cmd` steps.

### App Service for containers

- `WEBSITES_PORT` tells App Service which port your container listens on. Container starts but requests time out → this is almost always missing.
- Private registry pull uses `DOCKER_REGISTRY_SERVER_URL` / `_USERNAME` / `_PASSWORD`, or a managed identity instead of the username/password pair.
- App settings surface as **environment variables**. On Linux, hierarchical .NET keys use a double underscore: `Storage__ConnectionString`, not `Storage:ConnectionString`.
- Secrets belong in Key Vault, referenced from an app setting as `@Microsoft.KeyVault(SecretUri=https://…)`. Requires a managed identity with **Key Vault Secrets User**.
- **Deployment slots:** settings swap with the slot unless marked as a **deployment slot setting** (sticky), which stays put. Sticky is what you want for per-environment connection strings.
- `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true` to persist `/home` across restarts.

### Azure Container Apps

- **Revisions are immutable snapshots.** Revision-scope changes (image, env vars, resources) create a new revision. App-scope changes (ingress, secret values, registry credentials) apply to all revisions in place.
- **Traffic splitting requires multiple-revision mode.** In single-revision mode all traffic goes to the latest revision — that is the answer to every blue/green and canary question.
- **Scale rules:** HTTP (concurrent requests), TCP, or custom via any **KEDA scaler** — Service Bus queue length, Event Hubs lag, Redis list length, Kafka, cron, Azure Storage queues.
- `minReplicas: 0` gives scale-to-zero. Note that a container with no ingress and no active scale rule stays at zero — background workers need an explicit KEDA rule to ever wake up.
- Secrets are defined on the app and referenced from env vars as `secretref:mysecret`. KEDA scale rules reference them through an `auth` block, not inline.
- The **environment** is the security and networking boundary: shared VNet, shared Log Analytics workspace, and apps inside it reach each other by name.
- Logs: `ContainerAppConsoleLogs_CL` is your app's stdout/stderr; `ContainerAppSystemLogs_CL` is the platform telling you why a revision failed to start.

### AKS

- **Pod status decoder:** `ImagePullBackOff` = registry auth or wrong tag. `CrashLoopBackOff` = container starts then exits, read the logs. `Pending` = scheduler cannot place it, usually resource requests or node selectors. Exit code **137** = OOMKilled, raise the memory limit.
- **Liveness** probe failing restarts the container; **readiness** probe failing only pulls it out of the Service endpoints. A pod flapping in and out of rotation is a readiness problem.
- Troubleshooting order: `kubectl get pods` → `kubectl describe pod` (events, why it will not start) → `kubectl logs --previous` (why the last one died) → `kubectl port-forward` to bypass Service/Ingress and prove the container itself answers.
- **Workload identity** with a federated credential is the current way to give pods an Azure identity. Pod-managed identity is deprecated.
- Service types: `ClusterIP` internal only, `NodePort` via node IPs, `LoadBalancer` gets an Azure load balancer and public IP.

## Cosmos DB for NoSQL | part of 25–30%

- **Five consistency levels**, strongest first: Strong, Bounded Staleness, **Session (default)**, Consistent Prefix, Eventual. Reads at Strong and Bounded Staleness cost **2× the RUs** of the weaker three.
- A 1 KB point read costs **1 RU**. Check the real cost with `response.RequestCharge` (the `x-ms-request-charge` header).
- **429** = throttled. The SDK retries automatically; tune with `MaxRetryAttemptsOnRateLimitedRequests`. Persistent 429s on one logical partition mean a **hot partition** — fix the partition key, not the throughput.
- **Indexing policy:** everything is indexed by default. **Excluding** paths you never filter on is the standard way to cut write RU charges. **Composite indexes** are required for `ORDER BY` on two or more properties.
- **Vector search:** declare a container vector policy (path, `float32`, dimensions, distance function — cosine / dotproduct / euclidean) plus a vector index: `flat` (exact, small sets), `quantizedFlat`, or `diskANN` (large sets). Query with `VectorDistance()` in `ORDER BY`.
- **Change feed, latest-version mode** (default): inserts and updates only, and you see only the final state of an item. **It does not surface deletes** — either use all-versions-and-deletes mode, or soft-delete with a TTL.
- The **change feed processor** needs a separate **lease container** to track checkpoints and distribute partitions across instances. Scaling out is just running more instances against the same lease container.
- `CosmosClient` is thread-safe and expensive to build — register it as a **singleton**. **Direct** mode gives lower latency than gateway; `AllowBulkExecution` for high-volume writes.
- **Autoscale** throughput scales between 10% and 100% of max and costs 1.5× per RU compared to manual — worth it for spiky loads, not for steady ones.
- Always pass the partition key in `QueryRequestOptions` when you know it. A cross-partition fan-out query is the usual cause of a query costing hundreds of RUs.

## PostgreSQL & pgvector | part of 25–30%

- Enable the extension in two steps: add `vector` to the **azure.extensions** server parameter (allowlist), then run `CREATE EXTENSION vector;` in the database. Doing only the second fails.
- **Distance operators:** `<->` L2/Euclidean, `<=>` cosine, `<#>` negative inner product. The index you build must use the matching operator class (`vector_cosine_ops` etc.) or the planner will ignore it.
- **HNSW vs IVFFlat:** HNSW gives better recall and query latency, can be built on an empty table, but uses more memory and builds slower. IVFFlat builds fast and is smaller, but **needs representative data present before you create it**.
- Recall/speed knobs at query time: `SET hnsw.ef_search = 100;` or `SET ivfflat.probes = 10;`. Higher = better recall, slower query.
- Build-time knobs: HNSW `m` and `ef_construction`; IVFFlat `lists` (rule of thumb: rows/1000 up to 1M rows, then √rows). Raise `maintenance_work_mem` to keep an index build in memory.
- **Metadata-filtered RAG:** a plain `WHERE tenant_id = $1` alongside a vector `ORDER BY` can under-return, because the ANN index is searched first. Use a **partial index** per filter value, or iterative scans, when filters are selective.
- Vector workloads are memory-bound: prefer **memory-optimized** SKUs and size RAM so the index fits, otherwise every query hits disk.
- **Connection handling:** flexible server has **PgBouncer built in on port 6432** (transaction pooling). Serverless callers that open a connection per invocation should go through it. Each idle Postgres connection still costs memory.
- `EXPLAIN ANALYZE` is how you prove the vector index is being used — a `Seq Scan` means it is not.
- `halfvec` halves index memory at a small recall cost; `pg_diskann` is the Azure extension for very large vector sets.

## Managed Redis | part of 25–30%

- **Tiers:** Memory Optimized (most RAM per vCPU), Balanced, Compute Optimized (highest throughput, best for vector search), Flash Optimized (NVMe, large cold datasets).
- **Cache-aside:** read cache → miss → read source → write to cache with a TTL. Always set expiry; `SET key val EX 300` in one call beats `SET` then `EXPIRE`.
- **Invalidate on write.** Deleting the key on update is safer than updating it — the next read repopulates it, and you cannot leave a stale value behind on a failed write.
- **Eviction policies:** `volatile-lru` evicts only keys with a TTL, `allkeys-lru` for a pure cache, `noeviction` makes writes fail once memory is full. Keys vanishing early → wrong eviction policy or too little memory.
- **Vector search** uses the RediSearch module: `FT.CREATE` with a `VECTOR HNSW` or `FLAT` field (`TYPE FLOAT32`, `DIM`, `DISTANCE_METRIC COSINE`), then a KNN query `*=>[KNN 5 @vec $blob AS score]`.
- Never run `KEYS` against a production cache — it blocks the server. Use `SCAN`.
- Prefer Microsoft Entra ID auth over access keys; use one multiplexed connection (a singleton `ConnectionMultiplexer`) rather than a connection per request.

## Service Bus & Event Grid | part of 20–25%

### Service Bus

- **Four ways into the dead-letter queue:** delivery count exceeds `MaxDeliveryCount` (default **10**), the message TTL expires, a subscription filter throws, or the app calls `DeadLetterMessageAsync`. Read it at `<queue>/$DeadLetterQueue`.
- **PeekLock** (default) = at-least-once: complete, abandon, defer or dead-letter it yourself. **ReceiveAndDelete** = at-most-once, message is gone even if you crash.
- Lock duration maxes at **5 minutes**. Long-running handlers must call `RenewMessageLockAsync` (or let the processor auto-renew) or the message reappears and gets processed twice.
- **Sessions** are the only way to get FIFO ordering and per-entity state. Set `SessionId` on send and use a session processor to receive.
- **Duplicate detection** keys on `MessageId` within a configured window — you must set MessageId yourself for it to do anything.
- **Subscription filters:** SQL filters (expressions over properties), correlation filters (cheapest, exact match on system/user properties), and boolean true/false.
- Message size: **256 KB** on Basic and Standard. Premium defaults to **1 MB** and can be raised to **100 MB**, but only over AMQP. Large payloads go to Blob Storage with a claim-check pointer in the message.
- Throughput knobs on the processor: `MaxConcurrentCalls` and `PrefetchCount`.

### Event Grid

- **Delivery is at-least-once, so handlers must be idempotent.** Retries use exponential backoff, up to **30 attempts** over a **24-hour** TTL by default.
- **Dead-lettering goes to a Storage blob container** and must be configured explicitly — without it, undeliverable events are dropped silently.
- **Filtering:** event type, subject begins-with/ends-with, and advanced filters on any payload field (`NumberGreaterThan`, `StringContains`, …). Filter at the subscription, not in your handler.
- **Webhook endpoints must complete a validation handshake** — echo back the `validationCode` or call the `validationUrl`. Azure services as handlers (Functions with the Event Grid trigger, Service Bus, storage queues) skip this.
- Schemas: Event Grid schema or **CloudEvents 1.0**. CloudEvents is the interoperable choice.
- **Choosing between them:** Event Grid = discrete reactive notifications ("a blob appeared"). Event Hubs = high-throughput telemetry streams with replay. Service Bus = ordered, transactional business messages that must not be lost.

## Azure Functions | part of 20–25%

- **A function has exactly one trigger** and any number of input/output bindings.
- **Timeouts:** Consumption defaults to 5 minutes, hard cap **10 minutes**. Premium and Flex Consumption default to 30 and are effectively unbounded. "Job runs longer than 10 minutes" → not Consumption, or use Durable Functions.
- Regardless of `functionTimeout`, an HTTP-triggered function must respond within **230 seconds** — the load balancer's idle timeout. Longer work needs the async pattern.
- **Premium / Flex** buy you VNet integration, always-ready instances (no cold start) and longer runs. Classic Consumption supports neither VNet integration nor always-ready instances, and is now a legacy plan.
- **Timer triggers use six-field NCRONTAB** starting with **seconds**: `0 */5 * * * *` is every five minutes, not every five seconds.
- **HTTP auth levels:** `anonymous`, `function` (function or host key), `admin` (master key). Keys are not identity — put Entra ID or APIM in front for real auth.
- **Identity-based connections** replace connection strings: `ServiceBusConn__fullyQualifiedNamespace`, `Storage__accountName`, `Cosmos__accountEndpoint`, plus a managed identity with the right data-plane role.
- `local.settings.json` is local-only and never deployed. `host.json` holds host-wide config (batch sizes, retries, timeout) and does ship.
- **Durable Functions patterns:** function chaining, fan-out/fan-in, async HTTP API, monitor, human interaction, aggregator.
- **Orchestrator code must be deterministic** — it is replayed. No `DateTime.Now` (use `context.CurrentUtcDateTime`), no random, no I/O, no direct HTTP. Do that work in activity functions.
- For .NET, the **isolated worker model** is the supported one. Deploy with `WEBSITE_RUN_FROM_PACKAGE=1` for an atomic, read-only deployment.

## Key Vault & App Config | part of 20–25%

- **Key Vault RBAC over access policies.** `Key Vault Secrets User` = get/list secrets, which is all an app needs. `Secrets Officer` can write. Access policies are the legacy model.
- **Soft delete is always on** and cannot be turned off. **Purge protection** is optional and, once enabled, cannot be turned off.
- Secrets are **versioned**. Omit the version in the URI to always get the current one — that is what makes rotation transparent to callers.
- **Rotation:** set a rotation policy, and Key Vault raises `SecretNearExpiry` through Event Grid; a Function handles the event and writes the new version.
- **Cache secrets in the client.** Key Vault is throttled per vault; fetching a secret on every request is a documented anti-pattern.
- `DefaultAzureCredential` walks environment → workload identity → managed identity → Azure CLI, so the same code works locally and in Azure. With a **user-assigned** identity you must pass the client id.
- **App Configuration** holds non-secret config; **labels** are how you separate dev/test/prod values for the same key. It can also store **Key Vault references** so apps read one place.
- **Dynamic refresh needs a sentinel key:** register one key for refresh with a cache expiry, update it last after changing everything else, and the whole configuration reloads at once.
- **Feature flags** live in App Configuration and are read through `IFeatureManager.IsEnabledAsync`. Filters: percentage, time window, targeting.

## OpenTelemetry & KQL | part of 20–25%

- Wire up with the Azure Monitor distro: `builder.Services.AddOpenTelemetry().UseAzureMonitor();` plus `APPLICATIONINSIGHTS_CONNECTION_STRING`. Instrumentation keys are retired.
- **Three signals:** traces (spans), metrics, logs. A **trace** is one end-to-end operation; a **span** is one unit of work inside it.
- **Context propagates over the W3C `traceparent` header.** If a downstream service shows up as a separate trace, propagation is broken — usually a hand-rolled HttpClient or a queue hop that did not carry the context.
- Custom spans in .NET come from an `ActivitySource`; the resource attribute `service.name` becomes `cloud_RoleName` in Application Insights, and `service.instance.id` becomes `cloud_RoleInstance`.
- **Sampling** is the lever for cost. Fixed-rate sampling keeps whole traces together, so retained transactions stay diagnosable.
- **KQL shape:** `Table | where … | summarize … | order by … | take …`. Put the **time filter first** — it is the single biggest performance lever.
- Bucketing over time: `summarize count() by bin(timestamp, 5m)`. Failures: `requests | where success == false`. Correlate one operation across services with `where operation_Id == "…"`.
- `has` is term-indexed and fast; `contains` is a substring scan and slow. Avoid bare `search` across all tables.
- Custom properties arrive as dynamic: `extend tenant = tostring(customDimensions.tenantId)`.
- Tables: `requests` in, `dependencies` out, `traces` for ILogger output, `exceptions` for throws — all joined by `operation_Id`. Container Apps: `ContainerAppConsoleLogs_CL`. AKS: `ContainerLogV2`, `KubeEvents`.

## Common traps | read these twice

- **Cosmos DB control-plane RBAC does not grant data access.** "Cosmos DB Account Reader" or even Owner will not let you read documents — you need a **data-plane** role assignment created with `az cosmosdb sql role assignment create`. Highest-value trick question on the exam.
- **Change feed does not see deletes** in the default mode. Soft-delete with TTL, or use all-versions-and-deletes.
- **Traffic splitting silently does nothing in single-revision mode** on Container Apps.
- **An empty pgvector table + IVFFlat = a useless index.** Load data first, or use HNSW.
- **Event Grid drops undeliverable events** unless you configured a dead-letter blob container.
- **Setting `MessageId` is on you** — duplicate detection does nothing without it.
- **NCRONTAB has six fields.** A five-field cron expression copied from Linux will not mean what you think.
- **Reading a Key Vault secret per request will get you throttled.** Cache it.
- **A missing `WEBSITES_PORT`** is the reason a perfectly good container times out on App Service.
- **Strong consistency doubles your read RUs** — if a question asks how to cut cost without changing the partition key, look at the consistency level and the indexing policy.
