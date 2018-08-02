FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /app

ARG NUGET_URL=http://ubuntu-docker.dtes.tfl.gov.uk:8081/repository/nuget-hosted/
ARG NUGET_API_KEY=af861a5e-fd97-3926-9670-638218c35118
ARG BUILD_CONFIG=Release
ARG RuntimeBaseImage

# copy csproj and restore as distinct layers
COPY *.csproj .
RUN dotnet restore --source ${NUGET_URL}

# copy everything else and build app
COPY *.cs .
RUN dotnet publish -c Release -o out

FROM microsoft/dotnet:2.1-runtime AS runtime
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "MyApp.dll"]
