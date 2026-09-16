# Application logging

`ILogger` writes safe structured JSON to stdout and exports the same events through
OpenTelemetry .NET 1.18.0 to a TLS-enabled Collector. The Collector writes
OTel-native Elasticsearch data streams. Traces and metrics retain their existing
Aspire/general OTLP integration; they do not go to this logging Collector.

Reference versions: Elasticsearch/Kibana **9.5.4**, otelcol-contrib **0.153.0**,
OpenShift **4.20**, Logging **6.6** (`observability.openshift.io/v1`). No existing
cluster is upgraded by these assets. Validate actual installed versions, OTel
mappings, privileges and TLS trust before rollout; test version upgrades together.

## Run locally

Use Docker Compose v2, PowerShell, and Linux containers with at least 8 GiB memory
and sufficient free disk space. From the repository or generated project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/initialize-local.ps1
docker compose up --build -d
docker compose ps -a
docker compose logs -f web
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/verify-logging.ps1
```

Initialization creates a local CA, certificates and random password in
`.local/logging`. The one-shot setup service provisions application retention, a
limited ingestion API key and a Kibana service token. Secrets are not printed or
included in source, Docker build contexts or template packages. Protect this local
directory with your account's filesystem permissions; local bind-mounted files
must be readable by the different container UIDs. Do not reuse these credentials.

Trust `.local/logging/certs/ca.crt` in your development browser. Open
<https://localhost:8443> for the app and <https://localhost:5601> for Kibana.
Kibana's local administrator login is `elastic`, with the password stored in
`.local/logging/elastic-password`. This login is only for the disposable local stack.
The app retains Development authentication/fake sources; visit
`/auth/login?returnUrl=/scalar` to use its normal sign-in flow.

The smoke script sends API requests (expected 401 without authentication), checks
their correlation headers against Elasticsearch, verifies sensitive markers are
absent, checks retention, and creates Kibana data view **Application logs**
(`webapi-logs`, pattern `logs-webapi.otel-*`, time field `@timestamp`).
In **Discover**, select that view and **Last 15 minutes**. Add the resource,
severity, body, trace and correlation fields. Example KQL:

```text
resource.attributes.service.name : "webapi"
resource.attributes.deployment.environment.name : "Development"
attributes.CorrelationId : "<X-Correlation-ID response value>"
```

Confirm field names in Discover after mapping upgrades. `TraceId` and
`CorrelationId` are also copied to log attributes; OTLP's native trace ID remains.
Startup logs have null request IDs in stdout; OTLP may omit null attributes.
Service name, environment and version are present on all records.

`docker compose stop` and `docker compose down` retain the data volumes. Avoid
`down -v` unless deliberately discarding logs and queued records. Docker stdout
rotates at 20 MiB × 5 files per container, independently of Elasticsearch retention.

## Configuration and data policy

| Setting | Meaning |
| --- | --- |
| `OTEL_SERVICE_NAME` | Service name; defaults to `webapi` |
| `Observability__ServiceVersion` | Overrides assembly informational version; container build argument is `SERVICE_VERSION` |
| `ASPNETCORE_ENVIRONMENT` | `deployment.environment.name` |
| `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` | Full endpoint including `/v1/logs` for HTTP |
| `OTEL_EXPORTER_OTLP_LOGS_PROTOCOL` | Supplied deployments use `http/protobuf` |
| `OTEL_EXPORTER_OTLP_LOGS_HEADERS`, `..._TIMEOUT` | Override generic headers/timeout; timeout is milliseconds |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Existing all-signal destination; HTTP appends `/v1/<signal>` |
| `OTEL_EXPORTER_OTLP_TRACES_*`, `..._METRICS_*` | Independent signal overrides |
| `OTEL_EXPORTER_OTLP_CERTIFICATE` | PEM CA for the SDK exporter; SDK also supports client certificate/key settings |
| `CONFIG_SECRETS_PATH` | Key-per-file configuration directory; filenames use `__` separators |
| `ELASTICSEARCH_ENDPOINT`, `LOG_ENVIRONMENT` | Collector destination and data-stream namespace |

No endpoint means no OTLP exporter. A logs endpoint alone creates only a log
exporter. Generic plus log-specific settings create one log exporter at the
override destination. Supplied deployments leave generic endpoints unset.
Supply credential-bearing headers through secret configuration only.

`SafeLogContent` is the shared Console/OTLP policy. Only reviewed static templates
and fields are rendered. Unknown bodies become `Log content suppressed by data
policy.`, retaining category, severity, event ID and safe exception type. Raw
exceptions, arbitrary scopes and arbitrary state objects are not serialized.
Add new useful events by reviewing their exact template and fields in this policy.
Do not interpolate customer data into templates or place secrets in category names
or service metadata. Collector filtering is defense in depth, not a free-text
redactor for unrelated applications; network policy restricts OTLP ingress.

Bodies, query strings, cookies, tokens, connection strings, customer records and
raw exception messages/stacks are excluded. Correlation comes from the request
Activity, not a client-supplied `X-Correlation-ID`; existing response contracts
remain unchanged. All Console levels go to stdout. Resource attributes are
explicitly selected rather than exporting arbitrary environment metadata.

## Deploy to OpenShift

Defaults are namespaces `integration-test` and `integration-production`, existing
Elasticsearch/Kibana, and the cluster's default RWO storage class. Restricted SCC
assigns UIDs and volume groups; the workloads need no privileged containers or
cluster-admin permissions.

1. Build/push an immutable application image and set the overlay's Kustomize
   `images` replacement for `webapi`. Replace all `replace-*.example` URLs and the
   identity audience. If changing namespaces, update Collector URLs and forwarder
   exclusions too. Angular requires the existing OIDC client configuration.
2. Have the Elasticsearch administrator check `GET /` and the installed OTel
   templates. Create a separate bootstrap API key with cluster `monitor`,
   `manage_ilm`, and `manage_index_templates`. Put `bootstrap-api-key` and
   `elasticsearch-ca.crt` in a protected directory, then run:

   ```powershell
   powershell -File deploy/provision-elasticsearch.ps1 -Environment test -Endpoint https://YOUR-TEST-ES:9200 -SecretDirectory C:/secure/test-bootstrap
   powershell -File deploy/provision-elasticsearch.ps1 -Environment production -Endpoint https://YOUR-PRODUCTION-ES:9200 -SecretDirectory C:/secure/production-bootstrap
   ```

   Bootstrap resolves the installed OTel mappings and creates only this app's
   exact stream template and ILM policy. It does not modify shared templates.
   Create a different runtime key with `create_doc` and `auto_configure` on only
   `logs-webapi.otel-test` or `logs-webapi.otel-production`. Kibana users need their
   normal read/space permissions. Never mount bootstrap credentials at runtime.
3. Create namespaces and environment-specific secrets through your secret manager
   or `oc create secret ... --from-file`. Do not commit rendered Secrets:

   | Secret | Required file keys |
   | --- | --- |
   | `logging-elasticsearch` | `elasticsearch-api-key`, `elasticsearch-ca.crt` |
   | `webapi-configuration` | `ConnectionStrings__CustomerRegistry`, `Sources__Billing__ApiKey`, other required app secrets such as `Authentication__ClientSecret` and `MediatR__LicenseKey` |

   ```powershell
   oc create secret generic logging-elasticsearch -n integration-test --from-file=elasticsearch-api-key=C:/secure/test/ingestion-key --from-file=elasticsearch-ca.crt=C:/secure/test/ca.crt
   oc create secret generic webapi-configuration -n integration-test --from-file=C:/secure/test/application
   ```

   Service annotations create `webapi-tls` and `otel-collector-tls`; OpenShift injects
   its service CA into `service-ca`. The re-encrypt Route uses the router's default
   service-serving CA. Confirm ingress trusts it; otherwise set the Route's
   `destinationCACertificate` to that CA. Browser TLS uses the router certificate.
   The Collector has no public Route.
4. **Exclude stdout from every Elasticsearch ingestion path before enabling OTLP.**
   Merge `deploy/openshift/forwarding/elasticsearch-input.example.yaml` into each
   existing Elasticsearch-bound forwarder. Preserve unrelated pipelines/outputs.
   Replace built-in `application` input references in ES-bound pipelines with the
   filtered input. A label/annotation alone is not an exclusion. Keep other
   destinations such as Loki according to platform policy. No filelog receiver is
   configured here, and `oc logs` remains available.
5. Render, inspect and server-validate before applying:

   ```powershell
   oc kustomize deploy/openshift/overlays/test
   oc apply --dry-run=server -k deploy/openshift/overlays/test
   oc apply -k deploy/openshift/overlays/test
   oc rollout status statefulset/otel-collector -n integration-test
   oc rollout status deployment/webapi -n integration-test
   oc logs deployment/webapi -n integration-test -f
   oc apply -n integration-test -k deploy/openshift/monitoring
   ```

   Repeat for production after test acceptance. Production has one application
   pod and two Collectors, separate retained PVCs, and preferred node spreading.
   Ensure two workers are available. Enable user-workload monitoring first.
6. Exercise an API request and correlate stdout and Kibana. Search **all** log
   streams for that event to detect an unexpected stdout copy. Audit every
   Elasticsearch-bound forwarder; local smoke tests cannot establish cluster-wide
   exclusions. Verify SCC admission, external authentication, source readiness,
   TLS, persistent storage and alert evaluation in the real cluster.

The one-replica test Collector's disruption budget blocks voluntary eviction;
scale to two for node maintenance, then drain before scaling back. A surviving
replica does not consume another pod's queue: recreate the failed StatefulSet
ordinal with its original PVC. Never scale down a nonempty queue or delete its PVC.

## Buffering, retention, alerts and recovery

Each Collector has a 4 GiB serialized queue and a 10 GiB PVC. Initial sizing assumes
500 records/second averaging 1 KiB with approximately 2× one-hour capacity margin.
Measure actual sizes and storage overhead. Queue capacity should be at least
`2 * records_per_second * bytes_per_record * 3600`; reserve at least 2.5× queue
capacity for storage/compaction. Docker named volumes have no 10 GiB quota; reserve
equivalent free disk. PVCs request 10 GiB.

Batching follows durable enqueue (1-second flush, 1 MiB maximum). File storage
fsyncs writes. The Elasticsearch exporter uses native `retry`, not generic
`retry_on_failure`: initial 5 seconds, maximum 60 seconds, 240 retries and
429/500/502/503/504 responses. The bulk-flush timeout is 75 minutes: in this pinned
exporter it bounds the complete request retry sequence, so a short timeout would
discard a durable batch before the one-hour outage budget. A stalled request can
occupy a consumer until that deadline; queue alerts remain essential.
Overflow blocks, but upstream timeouts or exhausted
SDK memory can still lose records. The SDK's 8192-record queue is not a durable
outbox. Shutdown grace periods allow flushing.

```powershell
powershell -File deploy/verify-logging.ps1 -Outage
```

This stops only local Elasticsearch, emits 50 requests, kills/restarts the
Collector with the same volume, restores Elasticsearch, and verifies recovery.
For one-hour qualification, extend the outage to 60 minutes and generate measured
production traffic; short smoke tests are not endurance certification. In a
disposable stack, also test a small queue, invalid API key, untrusted CA and
429/503 faults, then restore valid configuration. Do not run outage tests against
corporate services without their separate operational process.

ILM rolls over after one day or 25 GiB primary-shard size, then deletes after
7/14/30 days from rollover. Retention is not an exact per-record maximum age.
Inspect `GET /logs-webapi.otel-*/_ilm/explain` and `GET /_ilm/policy/webapi-*`.
Rerunning bootstrap updates application policies; migrating foreign streams or
policies requires a separate review.

Alerts cover unavailable Collectors, queue use above 70%/90%, enqueue/export
failures and low PVC space. Verify live series and notification delivery. The
PVC rule needs platform kubelet metrics; if user-workload Prometheus cannot access
them, the platform monitoring owner must install it in the platform scope.

Rotate API keys by creating a replacement, updating the Secret, rolling one
Collector ordinal at a time with its original PVC, verifying ingestion, then
revoking the old key. Restart affected workloads after certificate rotation;
do not assume all components hot-reload mounted certificates. Protect buffer
volumes and private keys at rest.

Rollback images/configuration while preserving data and queues. Check persistent
storage compatibility before Collector downgrades; drain first if formats differ.
To return to stdout ingestion, disable and drain OTLP **before** removing the
forwarder exclusion. Never enable both Elasticsearch paths together.

Persistent queues protect accepted records, not application crashes before export,
disk loss, exhausted capacity, permanent indexing failures or retry exhaustion.
Ambiguous acknowledgements can duplicate retries. Source exclusion prevents
**dual-path** duplicates; it does not promise exactly-once delivery.

References: [Elasticsearch exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.153.0/exporter/elasticsearchexporter/README.md),
[file storage](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.153.0/extension/storage/filestorage/README.md),
[.NET OTLP exporter](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md).
