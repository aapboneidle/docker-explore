# Dockerfile for building a nuget package
#   NB Nuget packages depend on the target framework (e.g. .NET Core, .NET framework, .NET Standard),
#   but not on the target architecture (x64, arm, ...)
# Assumptions:
#   - only a single nuget source is required (Nexus allow hosted and proxy repos to be grouped)
FROM microsoft/dotnet:sdk AS build
WORKDIR /app
ARG NUGET_URL=http://ubuntu-docker.dtes.tfl.gov.uk:8081/repository/nuget-hosted/
ARG NUGET_API_KEY=af861a5e-fd97-3926-9670-638218c35118
ARG BUILD_CONFIG=Release

# copy csproj and restore as distinct layers
COPY *.csproj .
RUN dotnet restore --source ${NUGET_URL}

# copy everything else and build app
COPY *.cs .
RUN dotnet build -c ${BUILD_CONFIG} --no-restore
RUN dotnet nuget push bin/${BUILD_CONFIG}/*.nupkg --source ${NUGET_URL} --api-key ${NUGET_API_KEY}
