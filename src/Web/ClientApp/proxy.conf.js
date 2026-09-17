const { env } = require('process');

const target =
  env["services__webapi__https__0"] ||
  env["services__webapi__http__0"];

const PROXY_CONFIG = [
  {
    context: [
      "/api", "/auth", "/signin-oidc", "/signout-callback-oidc",
      "/openapi",
      "/scalar", "/health", "/alive"
    ],
    target: target,
    secure: true,
  }
];

module.exports = PROXY_CONFIG;
