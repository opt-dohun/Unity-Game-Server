# Makefile for OpenAPI Generator Docker operations

.PHONY: help pull generate-unity clean

# Default target
help:
	@echo "Available targets:"
	@echo "  make pull          - Pull OpenAPI Generator Docker image"
	@echo "  make generate-unity - Generate Unity client code from OpenAPI spec"
	@echo "  make clean         - Remove generated client code"

# Pull the OpenAPI Generator Docker image
pull:
	docker pull openapitools/openapi-generator-cli

# Generate Unity client code
generate-unity:
	docker run --rm -v \${PWD}:/local openapitools/openapi-generator-cli generate \
	  -i /local/spec/openapi.yaml \
	  -g unity \
	  -o /local/client-generated/

# Clean generated files
clean:
	rm -rf client-generated/
	@echo "Generated client code removed"