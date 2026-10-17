FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Cloud Run sends traffic to the port in the PORT variable (8080 by default)
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Api.dll"]
