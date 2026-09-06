# Build the Vue bundle and ASP.NET host into one deployable image.
FROM node:22-alpine AS ui
WORKDIR /src/src/Enoch.Ui
COPY src/Enoch.Ui/package*.json ./
RUN npm ci
COPY src/Enoch.Ui/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS app
WORKDIR /src
COPY . .
COPY --from=ui /src/src/Enoch.Ui/dist ./src/Enoch.Api/wwwroot
RUN dotnet publish src/Enoch.Api/Enoch.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 ENOCH_DATA=/data
VOLUME ["/data"]
COPY --from=app /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Enoch.Api.dll"]
