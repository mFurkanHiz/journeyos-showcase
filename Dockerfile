# JourneyOS Showcase API — self-contained, no secrets, no outbound calls.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY JourneyOS.Showcase.slnx ./
COPY backend/ backend/
RUN dotnet publish backend/src/JourneyOS.Showcase.Api/JourneyOS.Showcase.Api.csproj \
    -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5100
EXPOSE 5100
ENTRYPOINT ["dotnet", "JourneyOS.Showcase.Api.dll"]
