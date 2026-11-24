# The app's production build, served by nginx, which also passes /api and /hubs on to the API
# container (ADR-0031). Built by docker-compose's `web` service; see "the whole app in Docker"
# in the README.
FROM node:24-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# The page's security policy must allow each script the build put on it (ADR-0060), or the
# browser would refuse to run it.
RUN npx ng build && node scripts/check-csp.mjs dist/frontend/browser/index.html

# nginx as a user without privileges, listening on 8080, like the API's image.
FROM nginxinc/nginx-unprivileged:1.30-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
EXPOSE 8080
