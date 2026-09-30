// `ng serve --configuration docker`: talks to the API container from
// `docker compose --profile api up` instead of `dotnet run`, through the dev server's proxy
// (proxy.docker.json).
export const environment = {
  production: false,
  apiUrl: '/api',
};
