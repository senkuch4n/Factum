FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS builder
WORKDIR /app
# URLs que usa el NAVEGADOR (no el contenedor): se hornean en el bundle en `next build`.
# Para la instalación de una sola PC los valores correctos son los defaults.
ARG NEXT_PUBLIC_BACKEND_URL=http://localhost:8080
ARG NEXT_PUBLIC_AGENT_URL=http://localhost:8765
ENV NEXT_PUBLIC_BACKEND_URL=$NEXT_PUBLIC_BACKEND_URL \
    NEXT_PUBLIC_AGENT_URL=$NEXT_PUBLIC_AGENT_URL \
    NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

FROM node:22-alpine AS runner
WORKDIR /app
# HOSTNAME=0.0.0.0 es obligatorio: sin eso server.js escucha en el hostname del contenedor
# y el healthcheck a 127.0.0.1 falla.
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3000 HOSTNAME=0.0.0.0
# output: "standalone" no copia public/ ni .next/static: se copian a mano.
COPY --from=builder --chown=node:node /app/.next/standalone ./
COPY --from=builder --chown=node:node /app/.next/static ./.next/static
COPY --from=builder --chown=node:node /app/public ./public
USER node
EXPOSE 3000
CMD ["node", "server.js"]
