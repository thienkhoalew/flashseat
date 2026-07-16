# Azure deployment

## Suggested topology

- Azure Static Web Apps or public Container App: React frontend.
- Public Azure Container App: Gateway.
- Internal Container Apps: Identity, Events, Booking, Payment, Notification Worker.
- Azure Database for PostgreSQL Flexible Server: four databases.
- Azure Cache for Redis; RabbitMQ container for portfolio demo.
- Azure Container Registry; Application Insights through OTLP.
- Key Vault references for JWT, database and broker credentials.

## Deployment outline

1. Build immutable images tagged with the Git commit SHA.
2. Push images to ACR.
3. Create secret references; never place values in manifests.
4. Run one migration job before scaling application replicas.
5. Deploy internal services, then Gateway and web.
6. Configure Gateway CORS to the exact web origin.
7. Smoke-test health endpoints and the booking flow.

## Rollback

Redeploy the previous SHA image tags. Database migrations must remain backward compatible for one release.

## Cost control

Use minimum replicas of zero for demo services where cold starts are acceptable. Stop/delete the Container Apps environment, PostgreSQL server, Redis and registry after portfolio demonstrations. Local Docker Compose remains the zero-cloud-cost path.
