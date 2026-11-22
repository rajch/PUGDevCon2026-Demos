FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy solution and projects
COPY PayDesk.slnx ./
COPY src/PayDesk.Core/PayDesk.Core.csproj ./src/PayDesk.Core/
COPY src/PayDesk.Consolidator/PayDesk.Consolidator.csproj ./src/PayDesk.Consolidator/
COPY src/PayDesk.Web/PayDesk.Web.csproj ./src/PayDesk.Web/

RUN dotnet restore

# Copy all source
COPY src/ ./src/

# Build and publish both projects
RUN dotnet publish src/PayDesk.Web/PayDesk.Web.csproj -c Release -o /app/web
RUN dotnet publish src/PayDesk.Consolidator/PayDesk.Consolidator.csproj -c Release -o /app/consolidator

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/web ./web
COPY --from=build /app/consolidator ./consolidator

# Setup runtime directories
RUN mkdir -p /app/data && chown -R app:app /app

ENV ASPNETCORE_URLS="http://+:8080"
ENV Storage__DataDirectory="/app/data"
ENV Consolidator__ExecutablePath="/app/consolidator/PayDesk.Consolidator"

USER app
WORKDIR /app/web
EXPOSE 8080

ENTRYPOINT ["dotnet", "PayDesk.Web.dll"]
