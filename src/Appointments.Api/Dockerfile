# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj first and restore separately so Docker can cache this layer
# and skip it on rebuilds where only source files changed.
COPY Appointments.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# --- Runtime stage ---
# The much smaller ASP.NET runtime image (no SDK/compilers) is what actually
# ships — this is what keeps the final image small and reduces attack surface.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root user inside the container.
RUN useradd --uid 5678 --user-group --create-home appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Appointments.Api.dll"]
