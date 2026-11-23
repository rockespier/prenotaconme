# Caddy serving the Angular PWA and reverse-proxying /api and /health to the API.
# Build context: repository root.
FROM node:22-alpine AS build
WORKDIR /web
COPY src/frontend/prenotaconme-web/package.json src/frontend/prenotaconme-web/package-lock.json src/frontend/prenotaconme-web/.npmrc ./
RUN npm ci
COPY src/frontend/prenotaconme-web/ ./
RUN npx ng build

FROM caddy:2-alpine
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist/prenotaconme-web/browser /srv
