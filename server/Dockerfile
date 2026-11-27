# Build:  docker build -t coop-server server/
# Run:    docker run -d --restart unless-stopped -p 4100:4100 -e COOP_PASSWORD=changeme coop-server
FROM golang:1.22-alpine AS build
WORKDIR /src
COPY go.mod ./
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /coop-server ./cmd/coop-server

FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=build /coop-server /coop-server
EXPOSE 4100
ENTRYPOINT ["/coop-server"]
