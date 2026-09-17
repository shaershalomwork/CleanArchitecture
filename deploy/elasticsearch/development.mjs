import fs from 'node:fs';
import { client, provision } from './provision.mjs';

const deadline = setTimeout(() => { console.error('Initialization exceeded its five-minute deadline.'); process.exit(1); }, 300000);
let phase = 'loading initialization inputs';
try {
  const ca = fs.readFileSync(process.env.LOG_CA_FILE);
  const password = fs.readFileSync('/run/secrets/elastic-password', 'utf8').trim();
  const basic = `Basic ${Buffer.from(`elastic:${password}`).toString('base64')}`;
  const request = client(process.env.ELASTICSEARCH_ENDPOINT, ca, basic);
  if (process.argv[2] === 'elasticsearch') {
    console.log('Provisioning development logging policy and template.');
    phase = 'provisioning the policy and installed OTel template';
    const stream = await provision(request, 'development', 7, 0, true);
    phase = 'checking empty application storage';
    const initial = await request('GET', '/_resolve/index/logs-webapi.otel-development*?expand_wildcards=all');
    if (initial.indices?.length || initial.data_streams?.length) throw new Error('Application log storage was not empty.');
    phase = 'creating and authenticating service credentials';
    const key = await request('POST', '/_security/api_key', {name: 'development-collector', role_descriptors: {ingest: {
      cluster: [], indices: [{names: [stream], privileges: ['auto_configure', 'create_doc']}]
    }}});
    const token = await request('POST', '/_security/service/elastic/kibana/credential/token/aspire');
    await client(process.env.ELASTICSEARCH_ENDPOINT, ca, `ApiKey ${key.encoded}`)('GET', '/_security/_authenticate');
    await client(process.env.ELASTICSEARCH_ENDPOINT, ca, `Bearer ${token.token.value}`)('GET', '/_security/_authenticate');
    phase = 'writing this run’s credential handoff';
    for (const [name, value] of [['elasticsearch-api-key', key.encoded], ['kibana-token', token.token.value]]) {
      fs.writeFileSync(`/handoff/${name}.tmp`, value, {mode: 0o600});
      fs.renameSync(`/handoff/${name}.tmp`, `/handoff/${name}`);
    }
    console.log('Empty application storage verified; new ingestion and service credentials authenticated.');
  } else if (process.argv[2] === 'kibana') {
    phase = 'checking Kibana status and creating the data view';
    const kibana = client(process.env.KIBANA_ENDPOINT, ca, basic);
    const status = await kibana('GET', '/api/status');
    if (status.status?.overall?.level !== 'available') throw new Error('Kibana is unavailable.');
    await kibana('POST', '/api/data_views/data_view', {data_view: {
      id: 'application-logs', name: 'Application logs', title: 'logs-webapi.otel-development', timeFieldName: '@timestamp'
    }, override: true});
    console.log('Application logs data view is ready.');
  } else throw new Error('Unknown initialization mode.');
} catch {
  console.error(`Development initialization failed while ${phase}; check service health, TLS trust and access.`);
  process.exitCode = 1;
} finally { clearTimeout(deadline); }
