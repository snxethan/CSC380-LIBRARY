# Enhancement Recommendation: System Logs and Observability

## Current System Architecture

Before evaluating which enhancement to add, it is important to understand what the system
already contains.

| Service | Technology | Role |
|---|---|---|
| `api1`, `api2` | ASP.NET Core 10 + SQLite | REST API with HATEOAS (Users, Games, TradeOffers) |
| `nginx` | Nginx | Round-robin load balancer in front of both API replicas |
| `kafka` + `zookeeper` | Confluent Kafka 7.6.1 | Async message broker for notifications |
| `notification-worker` | .NET 10 console app | Kafka consumer — logs email events to stdout |
| `prometheus` | Prometheus | Metrics server (currently scrapes **only itself**) |
| Grafana | *(being added)* | Dashboard visualization layer |

The system already demonstrates: a hypermedia REST API, horizontal scaling with a load
balancer, and an asynchronous side-effect service driven by a message broker.
Prometheus has been added to the stack but its configuration only points at itself, and
Grafana has not yet been wired up.

---

## Evaluation of Every Option

### Option 1 — Microservices Expansion (new API or auth service)

**What it would mean:** Extract the existing controllers into separate services, or add a
new microservice such as a dedicated authentication/JWT service or a game-search service.

**Strengths:**
- Demonstrates service decomposition, a classic distributed systems pattern.
- A proper JWT authentication service would fix the existing plain-text password
  comparison used in `TradeOffersController` and `UserController`.

**Weaknesses:**
- Would require either a major refactoring of the existing codebase (splitting controllers
  into separate deployments) or building an entirely new service that touches little of
  the existing code.
- The system is already split by concern at the controller level; adding another service
  without a clear business need produces overhead without proportional benefit.
- The existing `UserController` already handles registration; a separate auth service
  would be duplicative without a real-world multi-tenant requirement to justify it.

**Verdict:** Medium value. Justifiable, but forces significant churn across the existing
codebase.

---

### Option 2 — Side-Effect Services (enrich the NotificationWorker)

**What it would mean:** Make the `NotificationWorker` actually send emails (e.g., via
SMTP or SendGrid) instead of only printing to the console. Could also add an
activity-log service that persists every domain event to a separate database.

**Strengths:**
- Directly improves an obvious gap: notifications are produced and consumed but never
  actually delivered.
- Low risk — no changes to the API or infrastructure topology.

**Weaknesses:**
- Requires an external email-sending dependency (SMTP credentials, SendGrid API key)
  that complicates local development and testing.
- Does not add a new distributed systems concept; the message-queue pattern is already
  present.
- The learning value for a distributed systems course is limited compared to the other
  options.

**Verdict:** Low-to-medium value for a distributed systems class. Good polish, but not a
meaningful architectural advancement.

---

### Option 3 — Distributed Caching with Redis

**What it would mean:** Introduce a Redis container and use it as a shared cache across
`api1` and `api2` for expensive or frequently read data (e.g., game listings, user
lookups, trade offer status).

**Strengths:**
- Redis is a canonical distributed systems component that demonstrates the
  read-through / cache-aside pattern.
- Would reduce contention on the shared SQLite volume that both API replicas mount.
- Demonstrates cache invalidation: when a trade is accepted and game ownership changes,
  cached game records must be evicted.

**Weaknesses:**
- SQLite on a shared Docker volume is already fast at the current scale; the performance
  gain would be real in principle but invisible in a local demo environment.
- Cache invalidation on trade acceptance (where ownership of two games changes
  atomically) is non-trivial to implement correctly and could introduce consistency bugs.
- Adds a new infrastructure dependency without addressing the most glaring existing
  gap: that Prometheus is running but monitoring nothing.

**Verdict:** Good distributed systems concept, moderate implementation complexity, but
solves a problem that does not yet exist at the current scale.

---

### Option 4 — System Logs and Observability ✅ **RECOMMENDED**

**What it would mean:** Fully integrate every service in the system with Prometheus
metrics, wire up Grafana dashboards, and add structured logging across the API and
worker.

**Strengths — see detailed section below.**

---

### Option 5 — Service Load Balancing and Failover (Nginx + Kubernetes)

**What it would mean:** Replace the Docker Compose deployment with a Kubernetes cluster,
add a Kubernetes Ingress controller, and configure horizontal pod autoscaling.

**Strengths:**
- Kubernetes is the industry standard for container orchestration and demonstrates true
  production-grade load balancing and failover.

**Weaknesses:**
- The system currently runs on Docker Compose. Migrating to Kubernetes is a large
  infrastructure change with nothing to do with the application code itself.
- Nginx already provides round-robin load balancing across two API replicas; the
  improvement to the demo would not be visible.
- Kubernetes adds significant operational complexity (YAML manifests, kubeconfig,
  ingress controller installation) that is unlikely to be the focus of a distributed
  systems assignment.

**Verdict:** Too large a scope change relative to what is already working.

---

### Option 6 — Data Replication or Sharding

**What it would mean:** Partition the data across multiple database nodes, for example by
migrating from SQLite to PostgreSQL and adding a read-replica, or by sharding game
records across two database instances by some key (e.g., game ID modulo 2).

**Strengths:**
- True horizontal data scaling is a core distributed systems concept.
- Demonstrates partition tolerance and the CAP theorem in practice.

**Weaknesses:**
- SQLite is a single-file, single-writer database and does not support replication or
  sharding natively. The migration to PostgreSQL is a prerequisite, not the enhancement
  itself.
- The system's data volume (users, games, trade offers) is trivially small; sharding
  would be artificial.
- Cross-shard queries (e.g., "find all trades where the offered game belongs to shard 1
  and the requested game belongs to shard 2") would require either a query-federation
  layer or application-level scatter-gather logic, which is complex to implement
  correctly.

**Verdict:** High conceptual value but very high implementation cost with no natural fit
to the current SQLite-based data layer.

---

## Recommendation: System Logs and Observability

### Why This Is the Best Choice

**1. Prometheus is already in the stack but broken.**

The file `Prometheus/prometheus.yml` currently reads:

```yaml
scrape_configs:
  - job_name: "prometheus"
    static_configs:
      - targets: ["prometheus:9090"]
```

This means Prometheus scrapes only its own internal metrics. None of the application
services — `api1`, `api2`, `nginx`, `kafka`, or `notification-worker` — are being
monitored at all. The monitoring infrastructure already exists; it simply needs to be
completed.

**2. Grafana is already being added.**

Grafana without metrics to display is an empty dashboard. Completing the observability
story gives Grafana something meaningful to visualize and makes the final demo
compelling.

**3. Every service in the system needs metrics, demonstrating the distributed aspect.**

A single-service metrics integration would be a tutorial exercise. This system has five
distinct services that each contribute differently to the overall workload:

| Service | Key metrics to expose |
|---|---|
| `api1`, `api2` | HTTP request rate, latency (P50/P95/P99), error rate, active DB connections, trade offers created/accepted/rejected, users registered |
| `nginx` | Requests per second per upstream, upstream error rate, active connections |
| `notification-worker` | Kafka messages consumed per second, processing errors, consumer group lag |
| `kafka` | Topic throughput, partition lag |
| `prometheus` | Already self-monitored |

Instrumenting all of them at once demonstrates how observability works across a
distributed system — something no single-service solution could show.

**4. The implementation has a clear, contained scope.**

Unlike migrating to Kubernetes or sharding a database, completing the observability
stack requires:
- Adding one NuGet package (`prometheus-net.AspNetCore`) to the API project.
- Exposing a `/metrics` endpoint in `Program.cs`.
- Updating `prometheus.yml` to scrape `api1:8080`, `api2:8080`, and `nginx:9113`.
- Adding an `nginx-prometheus-exporter` sidecar in `docker-compose.yml`.
- Adding Grafana to `docker-compose.yml` with auto-provisioned datasources and
  dashboards.
- Adding a few counters/histograms to the `NotificationWorker`.

All existing services remain unchanged in their behavior.

**5. It demonstrates a real distributed systems concern: correlating events across
services.**

A key distributed systems challenge is understanding the causal chain of a request:
HTTP request arrives at Nginx → forwarded to api1 → Kafka message published →
consumed by notification-worker. Metrics at each hop let an operator verify the entire
chain is healthy and measure where latency is introduced. This is not possible with any
of the other enhancement options without also adding observability.

**6. The business metrics tell the domain story.**

Custom Prometheus counters for `trade_offers_created_total`,
`trade_offers_accepted_total`, `trade_offers_rejected_total`, and
`users_registered_total` turn abstract infrastructure graphs into something that
reflects the actual purpose of the system (a retro video game exchange). This is good
systems design practice.

---

## What the Full Implementation Looks Like

### Step 1 — Instrument the ASP.NET Core API

Add `prometheus-net.AspNetCore` to the `.csproj`:

```xml
<PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
```

In `Program.cs`, register the metrics middleware and expose the `/metrics` endpoint:

```csharp
using Prometheus;

// After builder.Build():
app.UseHttpMetrics();          // records request rate, duration, status codes
app.MapMetrics();              // exposes GET /metrics for Prometheus to scrape
```

Add custom business metrics in the controllers:

```csharp
// In TradeOffersController:
private static readonly Counter TradeOffersCreated =
    Metrics.CreateCounter("trade_offers_created_total", "Total trade offers created.");

private static readonly Counter TradeOffersAccepted =
    Metrics.CreateCounter("trade_offers_accepted_total", "Total trade offers accepted.");

private static readonly Counter TradeOffersRejected =
    Metrics.CreateCounter("trade_offers_rejected_total", "Total trade offers rejected.");
```

Call `TradeOffersCreated.Inc()` after saving a new offer, and similarly for accepted/
rejected outcomes in `RespondToOffer`.

---

### Step 2 — Instrument the NotificationWorker

Add `prometheus-net` to `NotificationWorker.csproj`:

```xml
<PackageReference Include="prometheus-net" Version="8.2.1" />
```

Start a metrics server and increment counters inside the consume loop:

```csharp
using Prometheus;

var metricsServer = new MetricServer(port: 9091);
metricsServer.Start();

var notificationsProcessed =
    Metrics.CreateCounter("notifications_processed_total", "Total notifications processed.");
var notificationErrors =
    Metrics.CreateCounter("notification_errors_total", "Total notification processing errors.");

// Inside the consume loop:
notificationsProcessed.Inc();
// On ConsumeException:
notificationErrors.Inc();
```

---

### Step 3 — Add an Nginx Prometheus Exporter

The standard `nginx/nginx.conf` already has an upstream log. Enable the Nginx stub
status module and add an exporter sidecar to `docker-compose.yml`:

```nginx
# In nginx.conf, inside the server block:
location /nginx_status {
    stub_status on;
    allow 127.0.0.1;
    deny all;
}
```

```yaml
# In docker-compose.yml:
nginx-exporter:
  image: nginx/nginx-prometheus-exporter:1.1.0
  command: --nginx.scrape-uri=http://nginx:8080/nginx_status
  networks:
    - exchange-net
```

---

### Step 4 — Update Prometheus Configuration

Replace the current `Prometheus/prometheus.yml` with:

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "prometheus"
    static_configs:
      - targets: ["prometheus:9090"]

  - job_name: "api"
    static_configs:
      - targets: ["api1:8080", "api2:8080"]
    metrics_path: /metrics

  - job_name: "notification-worker"
    static_configs:
      - targets: ["notification-worker:9091"]

  - job_name: "nginx"
    static_configs:
      - targets: ["nginx-exporter:9113"]
```

---

### Step 5 — Add Grafana to Docker Compose

```yaml
grafana:
  image: grafana/grafana:11.0.0
  ports:
    - "3000:3000"
  environment:
    GF_SECURITY_ADMIN_PASSWORD: admin
  volumes:
    - grafana-data:/var/lib/grafana
    - ./Grafana/provisioning:/etc/grafana/provisioning
  depends_on:
    - prometheus
  networks:
    - exchange-net

volumes:
  exchange-data:
  grafana-data:
```

Provision a Prometheus datasource automatically via
`Grafana/provisioning/datasources/prometheus.yml`:

```yaml
apiVersion: 1
datasources:
  - name: Prometheus
    type: prometheus
    url: http://prometheus:9090
    isDefault: true
```

Provision a default dashboard via
`Grafana/provisioning/dashboards/dashboard.yml` that references a JSON dashboard
file showing:
- HTTP request rate per instance (api1 vs api2) to confirm load balancing is working.
- P95 request latency per endpoint.
- Trade offer lifecycle counters (created / accepted / rejected) over time.
- Notification-worker message throughput and Kafka consumer lag.

---

## Summary

| Criterion | Observability | Redis Cache | New Microservice | Notification Email |
|---|---|---|---|---|
| Builds on existing work | ✅ Yes (Prometheus already present) | ❌ No | ❌ No | ✅ Partial |
| Demonstrates distributed systems concept | ✅ Yes (cross-service metrics) | ✅ Yes (cache-aside) | ✅ Yes (decomposition) | ❌ Limited |
| Visible, compelling demo output | ✅ Grafana dashboards | ❌ Hard to show | ❌ Hard to show | ❌ Requires real email |
| Scope is contained | ✅ Yes | ✅ Yes | ❌ Large | ✅ Yes |
| All existing services benefit | ✅ All 5 services | ❌ Only API | ❌ Only API | ❌ Only worker |
| No external credentials needed | ✅ Yes | ✅ Yes | ✅ Yes | ❌ Needs SMTP/API key |

**Completing the observability stack is the right enhancement.** It finishes what has
already been started, exercises every component in the distributed system, produces a
live Grafana dashboard that is immediately convincing in a demo, and introduces no
external dependencies or risky refactoring.
