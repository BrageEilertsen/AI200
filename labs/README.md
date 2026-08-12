# pgvector lab

A hands-on hour on the part of AI-200 that carries the most marks. *Develop AI solutions by
using Azure data management services* is 25–30% of the exam, and the pgvector questions in it
are about index behaviour — which is much easier to remember once you have watched recall
collapse and then fixed it.

Everything here runs locally in Docker. **No Azure resources, no API keys, no cost.** The
vectors are random rather than real embeddings, which is deliberate: the lessons are about how
the index behaves, and random vectors make the failure modes sharper and the setup instant.

> One caveat, stated up front and it genuinely matters: random uniform vectors are close to a
> worst case for approximate search, because there are no real clusters to find. Recall numbers
> here are worse than you would see with genuine embeddings, and in one place — rebuilding an
> IVFFlat index — the improvement you would expect on real data does not show up at all. That is
> called out where it happens rather than glossed over.
>
> Every number quoted below was measured on this exact setup, not estimated.

## What you will actually see

| Exam point | What you will do |
| --- | --- |
| IVFFlat needs data before it is built | Build on an empty table and read Postgres's own warning; measure 0/10 recall |
| `probes` is the recall dial | Watch recall go 0 → 4 → 10 out of 10 as you raise it |
| HNSW can be built on an empty table | Build it and get 7/10 with no tuning on the same data |
| Operator class must match the operator | Build a cosine index, query with L2, watch it fall back to `Seq Scan` |
| `EXPLAIN ANALYZE` proves index use | Read the plan rather than assuming |
| Selective filters make ANN under-return | Filter to a small tenant and get fewer rows than you asked for |
| Partial indexes fix that | Build one per tenant and watch it return the full ten |
| `probes` / `ef_search` trade recall for latency | Turn the dial and measure both |

## Start

Docker Desktop is blocked by policy on this machine, but the Docker **engine** is installed
inside WSL and works fine without it. Start the daemon there first:

```bash
wsl -d Ubuntu -u root -- service docker start
```

Then run everything from inside WSL, where the Windows drive is mounted under `/mnt/c`:

```bash
wsl -d Ubuntu -u root -- bash -lc "cd /mnt/c/Users/beilertsen/dev/learning/ai-200-trainer/labs && docker compose up -d"
```

```bash
wsl -d Ubuntu -u root -- bash -lc "cd /mnt/c/Users/beilertsen/dev/learning/ai-200-trainer/labs && docker compose exec db psql -U postgres -d lab"
```

The daemon does not survive the WSL distro shutting down, so if a later command reports
*"service db is not running"*, start it again with the first command above.

On a machine where Docker Desktop runs normally, plain `docker compose up -d` and
`docker compose exec db psql -U postgres -d lab` from this folder are all you need.

Everything below is typed into that `psql` session.

---

## 1. Enable the extension

```sql
CREATE EXTENSION IF NOT EXISTS vector;
\dx
```

On **Azure Database for PostgreSQL flexible server** this is a two-step job, and the exam tests
the order: `vector` must first be added to the **`azure.extensions`** server parameter, and only
then will `CREATE EXTENSION` succeed. Locally there is no allowlist, so it just works — remember
the extra step exists.

## 2. Create the table and a generator

```sql
CREATE TABLE chunks (
    id        bigserial PRIMARY KEY,
    tenant_id int    NOT NULL,
    content   text   NOT NULL,
    embedding vector(384) NOT NULL
);

-- VOLATILE matters: without it the planner would evaluate this once and give
-- every row the same vector.
CREATE OR REPLACE FUNCTION random_vector(dim int) RETURNS vector AS $$
    SELECT array_agg(random())::vector FROM generate_series(1, dim);
$$ LANGUAGE sql VOLATILE;
```

## 3. Build IVFFlat on an empty table — the trap

This is the single most testable pgvector fact, so do it in the wrong order first.

```sql
CREATE INDEX chunks_ivf ON chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);
```

Now load 50,000 rows, deliberately skewed: tenant 1 takes about 70% and tenants 2–21 share the
rest, so each of them holds only a few hundred. That skew is what makes the filtered-search
failure in step 6 show up.

```sql
INSERT INTO chunks (tenant_id, content, embedding)
SELECT
    CASE WHEN random() < 0.7 THEN 1 ELSE (random() * 19)::int + 2 END,
    'chunk ' || g,
    random_vector(384)
FROM generate_series(1, 50000) g;

ANALYZE chunks;
```

Notice what Postgres told you when the index was created:

```
NOTICE:  ivfflat index created with little data
DETAIL:  This will cause low recall.
HINT:  Drop the index until the table has more data.
```

That warning *is* the exam answer. The engine knows the centroids it just computed describe
nothing, because there was nothing to cluster.

### Measure recall

Pick a query vector, compute the true nearest neighbours by forcing a scan, then compare against
what the index returns.

```sql
CREATE TEMP TABLE q AS SELECT random_vector(384) AS v;

-- Ground truth: no index, exhaustive comparison.
SET enable_indexscan = off;
CREATE TEMP TABLE truth AS
SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10;
RESET enable_indexscan;

-- What the index actually finds.
SELECT count(*) AS hits_out_of_10
FROM (SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10) ann
JOIN truth USING (id);
```

**0 out of 10** on this data. The index finds none of the true neighbours.

### Rebuild — and read the result honestly

```sql
REINDEX INDEX chunks_ivf;

SELECT count(*) AS hits_out_of_10
FROM (SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10) ann
JOIN truth USING (id);
```

Still **0 out of 10** — and it is worth understanding why, because it is the caveat from the top
of this page biting. Rebuilding gives IVFFlat honest centroids, but with 50,000 *uniformly
random* vectors there are no real clusters to find, and the default `probes = 1` searches a
single list out of 100 — about 1% of the table. Real embeddings cluster, so on real data the
rebuild alone moves recall a great deal. Here you have to open the aperture instead:

```sql
SET ivfflat.probes = 10;   -- measured: 4 / 10
SET ivfflat.probes = 50;   -- measured: 10 / 10
RESET ivfflat.probes;
```

Re-run the recall query at each setting. Fewer, larger lists behave the same way — with
`lists = 10`, probes of 1 and 3 give 2/10 and 6/10.

**Exam takeaways, both of which this demonstrates:**

- IVFFlat derives its centroids from the data present at build time — build on an empty table
  and Postgres warns you outright. Load first, or use HNSW.
- `probes` is the query-time recall dial, and its right value depends on `lists` and on how well
  your data actually clusters.

## 4. HNSW builds fine on an empty table

```sql
DROP INDEX chunks_ivf;
CREATE INDEX chunks_hnsw ON chunks USING hnsw (embedding vector_cosine_ops);

SELECT count(*) AS hits_out_of_10
FROM (SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10) ann
JOIN truth USING (id);
```

**7 out of 10 with no tuning at all**, on the same hostile random data where IVFFlat needed 50
probes to catch up. No warning on creation either, because HNSW inserts each row into a
navigable graph as it arrives — build order genuinely does not matter.

Note how much longer the build took. That is the trade-off in one observation: better recall and
latency out of the box, slower builds and more memory.

## 5. Operator class must match the operator

The index above is `vector_cosine_ops`, which serves `<=>`. Query with L2 instead:

```sql
EXPLAIN ANALYZE
SELECT id FROM chunks ORDER BY embedding <-> (SELECT v FROM q) LIMIT 10;
```

```sql
EXPLAIN ANALYZE
SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10;
```

Measured: the first plans a **`Seq Scan`**, the second an **`Index Scan using chunks_hnsw`**. The index exists in both cases —
it simply cannot answer a question posed with the wrong operator.

**Exam takeaway:** `<=>` cosine → `vector_cosine_ops`, `<->` L2 → `vector_l2_ops`,
`<#>` inner product → `vector_ip_ops`. A mismatch is silent: you get correct results, slowly.

## 6. Selective filters make ANN under-return

Find your smallest tenant:

```sql
SELECT tenant_id, count(*) FROM chunks GROUP BY tenant_id ORDER BY count(*) LIMIT 5;
```

Then ask for ten of its chunks, nearest first:

```sql
SELECT count(*) AS rows_returned FROM (
    SELECT id FROM chunks
    WHERE tenant_id = 20            -- substitute your smallest tenant
    ORDER BY embedding <=> (SELECT v FROM q)
    LIMIT 10
) t;
```

Measured on this setup: **0 rows returned** when asking for ten, from a tenant holding roughly 400. The graph
traversal collects a bounded candidate set first and the `WHERE` clause is applied to those
candidates — most get discarded.

This is the single most common production surprise in filtered RAG, and it is on the exam.

### Fix one: widen the candidate pool

```sql
SET hnsw.ef_search = 500;

SELECT count(*) AS rows_returned FROM (
    SELECT id FROM chunks WHERE tenant_id = 20
    ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10
) t;

RESET hnsw.ef_search;
```

Measured: **6 of 10**. More candidates, more survivors — but still short, and slower. A dial, not a solution.

### Fix two: a partial index

```sql
CREATE INDEX chunks_hnsw_t20 ON chunks USING hnsw (embedding vector_cosine_ops)
WHERE tenant_id = 20;

EXPLAIN ANALYZE
SELECT id FROM chunks WHERE tenant_id = 20
ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10;
```

The plan now uses `chunks_hnsw_t20`, which contains *only* that tenant's vectors, so every
candidate it produces already passes the filter. Ten requested, **ten returned** — measured, and the plan confirms it uses `chunks_hnsw_t20`.

**Exam takeaway:** for a small, stable set of filter values, a partial index per value is the
robust answer. For many values, raise `ef_search` or use iterative scans.

## 7. Turn the recall/latency dial

```sql
\timing on
SET hnsw.ef_search = 10;
SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10;

SET hnsw.ef_search = 200;
SELECT id FROM chunks ORDER BY embedding <=> (SELECT v FROM q) LIMIT 10;
RESET hnsw.ef_search;
```

Re-run the recall count at each setting. Higher `ef_search` costs time and buys accuracy. The
IVFFlat equivalent is `ivfflat.probes`; both are query-time settings, whereas `m` /
`ef_construction` and `lists` are fixed when the index is built.

## 8. Memory: `halfvec`

```sql
SELECT pg_size_pretty(pg_relation_size('chunks_hnsw')) AS hnsw_size;

CREATE INDEX chunks_half ON chunks USING hnsw ((embedding::halfvec(384)) halfvec_cosine_ops);
SELECT pg_size_pretty(pg_relation_size('chunks_half')) AS halfvec_size;
```

Measured: **98 MB to 56 MB**. Each component is stored as a 16-bit float instead of 32-bit. That is the
answer when index memory is the binding constraint and a small recall loss is acceptable.

## Tear down

```bash
docker compose down -v
```

## Using Supabase instead

Supabase is Postgres with pgvector available, so every statement above works against it —
enable the extension from the dashboard (Database → Extensions → `vector`) or with
`CREATE EXTENSION vector;`, then connect with `psql` using the connection string from project
settings. Docker is used here only because it is faster to reset and costs nothing.

## What to carry into the exam

- IVFFlat centroids come from build-time data. **Load, then index** — or use HNSW.
- HNSW: better recall and latency, slower to build, more memory. Buildable on an empty table.
- Operator class must match the operator, or the index is silently ignored.
- `EXPLAIN ANALYZE` is how you prove it: `Index Scan` good, `Seq Scan` on a large table bad.
- Selective filter plus ANN under-returns. Partial index, or a wider candidate pool.
- Query-time dials: `hnsw.ef_search`, `ivfflat.probes`. Build-time: `m`, `ef_construction`, `lists`.
- On Azure, `vector` must be in the **`azure.extensions`** allowlist before `CREATE EXTENSION`.
- Vector workloads are memory-bound: low CPU with heavy disk reads means the index does not fit
  in RAM.
