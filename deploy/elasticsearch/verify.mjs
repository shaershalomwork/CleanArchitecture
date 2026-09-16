import fs from 'node:fs';
import https from 'node:https';
import crypto from 'node:crypto';

const ca = fs.readFileSync('/local/certs/ca.crt');
const auth = `Basic ${Buffer.from(`elastic:${fs.readFileSync('/local/elastic-password', 'utf8').trim()}`).toString('base64')}`;
const stateFile = '/local/verification.json';
const mode = process.argv[2] ?? 'smoke';
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
function request(url, {method = 'GET', body, headers = {}} = {}) {
  return new Promise((resolve, reject) => {
    const req = https.request(url, {method, ca, headers: {'Content-Type': 'application/json', ...headers}}, res => {
      let data = '';
      res.on('data', part => data += part);
      res.on('end', () => resolve({status: res.statusCode, headers: res.headers, body: data}));
    });
    req.on('error', () => reject(new Error(`Connection failed: ${new URL(url).hostname}`)));
    req.setTimeout(10000, () => req.destroy());
    req.end(body ? JSON.stringify(body) : undefined);
  });
}
async function es(path, options = {}) {
  const response = await request(`https://elasticsearch:9200${path}`, {...options, headers: {Authorization: auth}});
  if (response.status >= 300) throw new Error(`Elasticsearch verification failed: ${path} HTTP ${response.status}`);
  return JSON.parse(response.body);
}
async function emit(count) {
  const traces = [];
  for (let i = 0; i < count; i++) {
    const trace = crypto.randomBytes(16).toString('hex');
    const response = await request('https://web:8443/api/customers?token=private-smoke-marker', {headers: {
      traceparent: `00-${trace}-${crypto.randomBytes(8).toString('hex')}-01`,
      'X-Correlation-ID': 'untrusted-private-smoke-marker'
    }});
    if (response.status !== 401 || response.headers['x-correlation-id'] !== trace) throw new Error('Application response or correlation contract failed.');
    traces.push(trace);
  }
  fs.writeFileSync(stateFile, JSON.stringify({traces}));
  console.log(`Emitted ${traces.length} correlated application requests.`);
}
async function check() {
  const {traces} = JSON.parse(fs.readFileSync(stateFile));
  for (let attempt = 0; attempt < 90; attempt++) {
    const result = await es('/logs-webapi.otel-development/_search', {method: 'POST', body: {
      size: 1000, query: {terms: {'attributes.CorrelationId': traces}}
    }}).catch(() => null);
    if (result && result.hits.hits.length >= traces.length) {
      for (const trace of traces) {
        const hits = result.hits.hits.filter(h => h._source.attributes?.CorrelationId === trace &&
          JSON.stringify(h._source.body).includes('HTTP request completed'));
        if (hits.length !== 1) throw new Error(`Expected one request completion record for ${trace}; got ${hits.length}.`);
        const source = hits[0]._source;
        if (JSON.stringify(source).includes('private-smoke-marker')) throw new Error('Sensitive marker reached Elasticsearch.');
        if (!JSON.stringify(source).includes('service.name') && !source.resource?.attributes?.service?.name && !source.resource?.attributes?.['service.name']) throw new Error('Service metadata missing.');
      }
      console.log(`Verified ${traces.length} unique indexed request logs, matching correlation and no sensitive marker.`);
      return;
    }
    await sleep(2000);
  }
  throw new Error('Logs did not arrive within the recovery window.');
}
try {
  if (mode === 'emit' || mode === 'smoke') await emit(mode === 'emit' ? 50 : 3);
  if (mode === 'check' || mode === 'smoke') await check();
  if (mode === 'smoke') {
    const lifecycle = await es('/logs-webapi.otel-development/_ilm/explain');
    if (!Object.values(lifecycle.indices).every(index => index.policy === 'webapi-development' && index.managed)) throw new Error('Application ILM policy not applied.');
    const kibana = await request('https://kibana:5601/api/status', {headers: {Authorization: auth}});
    if (kibana.status !== 200) throw new Error(`Kibana is not ready: HTTP ${kibana.status}`);
    const view = await request('https://kibana:5601/api/data_views/data_view', {method: 'POST', headers: {Authorization: auth, 'kbn-xsrf': 'verification'}, body: {
      override: true, data_view: {id: 'webapi-logs', title: 'logs-webapi.otel-*', name: 'Application logs', timeFieldName: '@timestamp'}
    }});
    if (view.status !== 200) throw new Error(`Kibana data view creation failed: HTTP ${view.status}`);
    console.log('Verified application ILM policy and Kibana data view webapi-logs.');
  }
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
