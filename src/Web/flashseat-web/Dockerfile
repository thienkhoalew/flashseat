FROM node:22.12.0-alpine AS build
WORKDIR /app
COPY src/Web/flashseat-web/package*.json ./
RUN npm install
COPY src/Web/flashseat-web .
RUN npm run build
FROM nginx:1.27.3-alpine
COPY src/Web/flashseat-web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
