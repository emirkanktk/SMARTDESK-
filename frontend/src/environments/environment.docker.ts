// `ng serve --configuration docker`: talks to the API container from
// `docker compose --profile api up` instead of `dotnet run`.
export const environment = {
  production: false,
  apiUrl: 'http://localhost:8080/api',
};
