FROM mcr.microsoft.com/dotnet/sdk:8.0.404 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Workers/FlashSeat.Notification.Worker/FlashSeat.Notification.Worker.csproj -c Release -o /app --no-self-contained
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "FlashSeat.Notification.Worker.dll"]
