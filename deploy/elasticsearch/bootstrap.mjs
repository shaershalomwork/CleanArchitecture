import fs from 'node:fs';
import { client, provision } from './provision.mjs';

// Production/test command interface is unchanged. Development has its own job.
try {
  const request = client(process.env.ELASTICSEARCH_ENDPOINT,
    fs.readFileSync('/run/secrets/elasticsearch-ca.crt'),
    `ApiKey ${fs.readFileSync('/run/secrets/bootstrap-api-key', 'utf8').trim()}`);
  await provision(request, process.env.LOG_ENVIRONMENT, Number(process.env.LOG_RETENTION_DAYS), Number(process.env.LOG_REPLICAS ?? '1'));
} catch {
  console.error('Elasticsearch provisioning failed; check endpoint, trust, access and lifecycle configuration.');
  process.exitCode = 1;
}
