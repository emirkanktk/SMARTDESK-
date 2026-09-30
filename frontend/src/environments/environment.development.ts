// `ng serve`: the dev server proxies /api and /hubs to the API from `dotnet run` (proxy.conf.json),
// so the app and the API share an origin as in production, and the session cookie just works
// (ADR-0026).
export const environment = {
  production: false,
  apiUrl: '/api',
};
