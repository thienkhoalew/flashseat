FROM mcr.microsoft.com/dotnet/sdk:8.0.404 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Services/Payment/FlashSeat.Payment.Api/FlashSeat.Payment.Api.csproj -c Release -o /app --no-self-contained
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "FlashSeat.Payment.Api.dll"]
