import https from 'node:https';
import { setTimeout as poll } from 'node:timers/promises';

// Never include response bodies, headers or credentials in diagnostics.
export function client(endpoint, ca, authorization) {
  const base = new URL(endpoint);
  if (base.protocol !== 'https:' || base.username || base.password) throw new Error('Use HTTPS without URL credentials.');
  return (method, path, body) => new Promise((resolve, reject) => {
    const req = https.request(new URL(path, base), {
      method, ca, headers: { Authorization: authorization, 'Content-Type': 'application/json', 'kbn-xsrf': 'initialization' }
    }, res => {
      let content = '';
      res.on('data', chunk => { content += chunk; if (content.length > 8 * 1024 * 1024) req.destroy(new Error('Response limit exceeded.')); });
      res.on('error', () => reject(new Error('Service response was interrupted.')));
      res.on('end', () => {
        if (res.statusCode < 200 || res.statusCode >= 300) {
          const error = new Error(`Service request failed: HTTP ${res.statusCode}.`);
          error.status = res.statusCode;
          return reject(error);
        }
        try { resolve(content ? JSON.parse(content) : {}); } catch { reject(new Error('Invalid service response.')); }
      });
    });
    req.setTimeout(30000, () => req.destroy(new Error('Request deadline exceeded.')));
    req.on('error', () => reject(new Error('Service request failed; check endpoint, TLS trust and access.')));
    req.end(body === undefined ? undefined : JSON.stringify(body));
  });
}

export async function provision(request, environment, days, replicas, waitForMappings = false) {
  if (!/^(development|test|production)$/.test(environment) || !Number.isInteger(days) || days < 1 || !Number.isInteger(replicas) || replicas < 0)
    throw new Error('Invalid logging lifecycle configuration.');
  const version = (await request('GET', '/')).version?.number;
  if (!version?.startsWith('9.')) throw new Error('This deployment targets Elasticsearch 9.x.');
  const stream = `logs-webapi.otel-${environment}`;
  const policy = `webapi-${environment}`;
  await request('PUT', `/_ilm/policy/${policy}`, {policy: {phases: {
    hot: {actions: {rollover: {max_age: '1d', max_primary_shard_size: '25gb'}}},
    delete: {min_age: `${days}d`, actions: {delete: {}}}
  }}});
  const matches = pattern => new RegExp('^' + pattern.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replaceAll('*', '.*') + '$').test(stream);
  const deadline = Date.now() + (waitForMappings ? 120000 : 0);
  let source;
  do {
    try {
      const simulated = await request('POST', `/_index_template/_simulate_index/${stream}`);
      const templates = (await request('GET', '/_index_template')).index_templates;
      source = templates.filter(t => t.name !== policy && t.name.includes('otel') && t.index_template.index_patterns.some(matches))
        .sort((a, b) => (b.index_template.priority ?? 0) - (a.index_template.priority ?? 0))[0]?.index_template;
      if (simulated.template?.mappings?.properties?.['@timestamp'] && source?.composed_of?.length) break;
    } catch (error) {
      if (![400, 404, 503].includes(error.status)) throw error;
    }
    if (Date.now() >= deadline) throw new Error('Installed OTel template components are not ready.');
    console.log('Waiting for Elasticsearch to finish installing OTel template components.');
    await poll(1000);
  } while (true);
  await request('PUT', `/_index_template/${policy}`, {
    index_patterns: [stream], priority: Math.max(501, (source.priority ?? 0) + 1), data_stream: {},
    composed_of: source.composed_of,
    ignore_missing_component_templates: source.ignore_missing_component_templates ?? [],
    template: {settings: {'index.lifecycle.name': policy, 'index.number_of_replicas': replicas}}
  });
  console.log(`Configured ${stream}: Elasticsearch ${version}, retention ${days}d.`);
  return stream;
}
