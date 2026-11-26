# This is a multi-stage Dockerfile, as recommended by Microsoft.
# See:
#   https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0

# Build stage: uses an SDK image as base
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy solution and projects, but not all source code, and restore.
# This will ensure that dependencies (NuGet packages) are cached in
# the build cache.
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

# Final stage: builds the runtime image. Uses aspnet image as base,
# because the web application is primary.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install iputils-ping so ping works for non-root users
# Install basic font support for PDFSharp
RUN apt-get update && \
    apt-get install -y --no-install-recommends iputils-ping && \
    apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core

# Pre-create the mountpoint with correct user ownership, so that 
# some environments can copy these permissions to any mounted
# volume
RUN mkdir /app/data && chown app:app /app/data


COPY --from=build /app/web ./web
COPY --from=build /app/consolidator ./consolidator

# Set up configuration env variables
# The last one prevents a memory leak in the postgres driver. See:
#   https://vensas.de/en/blog/dotnet-10-native-memory-leak-linux
ENV ASPNETCORE_URLS="http://+:8080"
ENV Storage__DataDirectory="/app/data"
ENV Consolidator__ExecutablePath="/app/consolidator/PayDesk.Consolidator"
ENV PGGSSENCMODE="disable"

USER app
WORKDIR /app/web
EXPOSE 8080

ENTRYPOINT ["dotnet", "PayDesk.Web.dll"]
