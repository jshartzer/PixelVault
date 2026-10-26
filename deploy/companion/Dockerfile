# Build from the repository root: docker build -f deploy/companion/Dockerfile -t pixelvault-companion:0.13.1 .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/PixelVault.Tracking/ src/PixelVault.Tracking/
COPY src/PixelVault.Companion/ src/PixelVault.Companion/
RUN dotnet publish src/PixelVault.Companion/PixelVault.Companion.csproj -c Release -o /out --no-self-contained \
    && chmod -R a+rX /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Companion__DataRoot=/data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "PixelVault.Companion.dll"]
