import { InjectionToken } from '@angular/core';

/**
 * Origin of the CQ Pulse API. Empty means same origin: in development `npm start` proxies /api and /hubs
 * to https://localhost:7256 (proxy.conf.json); in production serve the client behind the same host as the API,
 * or provide an absolute origin here (the API must then list the client origin in Cors:AllowedOrigins).
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', { factory: () => '' });
