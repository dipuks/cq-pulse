/** Builds an unsigned JWT-shaped token for client tests (the client never verifies signatures; the API does). */
export function fakeJwt(expiresInSeconds: number): string {
  const encode = (value: object) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({ sub: 'user-1', exp: Math.floor(Date.now() / 1000) + expiresInSeconds })}.signature`;
}
