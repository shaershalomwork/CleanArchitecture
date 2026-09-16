import fs from 'node:fs';
import https from 'node:https';

// No credentials or response bodies are written to stdout, including on failure.
const endpoint = new URL(process.env.ELASTICSEARCH_ENDPOINT);
if (endpoint.protocol !== 'https:' || endpoint.username || endpoint.password) throw new Error('Use an HTTPS endpoint without embedded credentials.');
const environment = process.env.LOG_ENVIRONMENT;
const days = Number(process.env.LOG_RETENTION_DAYS);
const replicas = Number(process.env.LOG_REPLICAS ?? '1');
if (!/^(development|test|production)$/.test(environment) || !Number.isInteger(days) || days < 1 || !Number.isInteger(replicas) || replicas < 0) throw new Error('Invalid logging lifecycle configuration.');
const local = process.env.LOCAL_SETUP === 'true';
const secretRoot = local ? '/local' : '/run/secrets';
const ca = fs.readFileSync(local ? '/local/certs/ca.crt' : '/run/secrets/elasticsearch-ca.crt');
const authorization = local
  ? `Basic ${Buffer.from(`elastic:${fs.readFileSync(`${secretRoot}/elastic-password`, 'utf8').trim()}`).toString('base64')}`
  : `ApiKey ${fs.readFileSync(`${secretRoot}/bootstrap-api-key`, 'utf8').trim()}`;
function request(method, path, body) {
  return new Promise((resolve, reject) => {
    const req = https.request(new URL(path, endpoint), {method, ca, headers: {Authorization: authorization, 'Content-Type': 'application/json'}}, res => {
      let content = '';
      res.on('data', chunk => content += chunk);
      res.on('end', () => {
        if (res.statusCode < 200 || res.statusCode >= 300) {
          // Schema errors contain only the generated template, never authentication response data.
          const reason = path.startsWith('/_index_template/') && res.statusCode === 400
            ? ` (${JSON.parse(content).error?.root_cause?.[0]?.reason ?? 'invalid template'})` : '';
          return reject(new Error(`Elasticsearch ${method} ${path}: HTTP ${res.statusCode}${reason}`));
        }
        try { resolve(content ? JSON.parse(content) : {}); } catch { reject(new Error('Invalid Elasticsearch response')); }
      });
    });
    req.setTimeout(30000, () => req.destroy(new Error('Elasticsearch request timed out')));
    req.on('error', () => reject(new Error('Elasticsearch request failed; check endpoint, TLS trust, and access.')));
    req.end(body === undefined ? undefined : JSON.stringify(body));
  });
}

try {
  const version = (await request('GET', '/')).version?.number;
  if (!version?.startsWith('9.')) throw new Error('This deployment targets Elasticsearch 9.x.');
  const stream = `logs-webapi.otel-${environment}`;
  const policy = `webapi-${environment}`;
  await request('PUT', `/_ilm/policy/${policy}`, {policy: {phases: {
    hot: {actions: {rollover: {max_age: '1d', max_primary_shard_size: '25gb'}}},
    delete: {min_age: `${days}d`, actions: {delete: {}}}
  }}});
  // Verify installed mappings, then reuse their component references. Simulated settings
  // include private settings which Elasticsearch rejects if copied into a user template.
  const simulated = await request('POST', `/_index_template/_simulate_index/${stream}`);
  if (!simulated.template?.mappings?.properties?.['@timestamp']) throw new Error('OTel index mappings are unavailable; do not ingest until installed.');
  const templates = (await request('GET', '/_index_template')).index_templates;
  const matches = pattern => new RegExp('^' + pattern.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replaceAll('*', '.*') + '$').test(stream);
  const source = templates.filter(t => t.name !== policy && t.name.includes('otel') && t.index_template.index_patterns.some(matches))
    .sort((a, b) => (b.index_template.priority ?? 0) - (a.index_template.priority ?? 0))[0]?.index_template;
  if (!source?.composed_of?.length) throw new Error('Cannot identify installed OTel template components.');
  await request('PUT', `/_index_template/${policy}`, {
    index_patterns: [stream], priority: Math.max(501, (source.priority ?? 0) + 1), data_stream: {},
    composed_of: source.composed_of,
    ignore_missing_component_templates: source.ignore_missing_component_templates ?? [],
    template: {settings: {'index.lifecycle.name': policy, 'index.number_of_replicas': replicas}}
  });
  // Updating the policy affects existing backing indices which already reference it.
  if (local) {
    const keyFile = '/local/collector-secrets/elasticsearch-api-key';
    if (!fs.existsSync(keyFile) || fs.statSync(keyFile).size === 0) {
      const key = await request('POST', '/_security/api_key', {name: `webapi-collector-${environment}`, role_descriptors: {ingest: {
        cluster: [], indices: [{names: [stream], privileges: ['auto_configure', 'create_doc']}]
      }}});
      fs.writeFileSync(keyFile, key.encoded, {mode: 0o644});
    }
    const kibanaFile = '/local/kibana.yml';
    if (!fs.readFileSync(kibanaFile, 'utf8').includes('elasticsearch.serviceAccountToken:')) {
      const token = await request('POST', `/_security/service/elastic/kibana/credential/token/local-${Date.now()}`);
      fs.appendFileSync(kibanaFile, `elasticsearch.serviceAccountToken: ${JSON.stringify(token.token.value)}\n`);
    }
  }
  console.log(`Configured ${stream}: Elasticsearch ${version}, retention ${days}d. Runtime ingestion needs create_doc and auto_configure on this stream only.`);
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
