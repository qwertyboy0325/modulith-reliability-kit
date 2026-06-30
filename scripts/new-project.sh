#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  ./scripts/new-project.sh <ProductName> <ModuleName>

Example:
  ./scripts/new-project.sh AcmeCommerce Inventory

Arguments must be PascalCase identifiers: letters, numbers, and underscores, starting with a letter.
Run this after copying the template to a new project folder.
USAGE
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "$repo_root"

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

if [ "$#" -ne 2 ]; then
  usage
  exit 1
fi

product_name="$1"
module_name="$2"

validate_identifier() {
  local value="$1"
  local label="$2"
  if ! [[ "$value" =~ ^[A-Za-z][A-Za-z0-9_]*$ ]]; then
    echo "Invalid ${label}: ${value}" >&2
    echo "Use a PascalCase identifier such as AcmeCommerce or Inventory." >&2
    exit 1
  fi
}

to_snake() {
  printf '%s' "$1" \
    | sed -E 's/([a-z0-9])([A-Z])/\1_\2/g; s/-+/_/g' \
    | tr '[:upper:]' '[:lower:]'
}

validate_identifier "$product_name" "product name"
validate_identifier "$module_name" "module name"

template_product="ModulithReliabilityKit"
template_module="Catalog"
template_product_snake="modulith_reliability_kit"
template_module_snake="catalog"

product_snake="$(to_snake "$product_name")"
module_snake="$(to_snake "$module_name")"

if [ ! -f "src/${template_product}.sln" ]; then
  echo "This does not look like a fresh template checkout: src/${template_product}.sln was not found." >&2
  echo "If the project was already renamed, update names manually or run from a fresh copy." >&2
  exit 1
fi

if [ ! -d "src/Modules/${template_module}" ]; then
  echo "This does not look like a fresh template checkout: src/Modules/${template_module} was not found." >&2
  exit 1
fi

echo "Renaming product ${template_product} -> ${product_name}"
echo "Renaming module ${template_module} -> ${module_name}"

rename_path_segment() {
  local old="$1"
  local new="$2"
  find . \
    -path './ref' -prune -o \
    -path '*/bin' -prune -o \
    -path '*/obj' -prune -o \
    -path '*/.idea' -prune -o \
    -name "*${old}*" -print \
    | sort -r \
    | while IFS= read -r path; do
        target="$(dirname "$path")/$(basename "$path" | sed "s/${old}/${new}/g")"
        if [ "$path" != "$target" ]; then
          mv "$path" "$target"
        fi
      done
}

replace_in_files() {
  local old="$1"
  local new="$2"
  find . \
    \( -path './ref' -o -path '*/bin' -o -path '*/obj' -o -path '*/.idea' -o -path './.git' \) -prune -o \
    -type f -print \
    | while IFS= read -r file; do
        if LC_ALL=C grep -Iq . "$file"; then
          perl -pi -e "s/\\Q${old}\\E/${new}/g" "$file"
        fi
      done
}

replace_in_files "$template_product" "$product_name"
replace_in_files "$template_product_snake" "$product_snake"
replace_in_files "$template_module" "$module_name"
replace_in_files "$template_module_snake" "$module_snake"

rename_path_segment "$template_product" "$product_name"
rename_path_segment "$template_module" "$module_name"

echo
echo "Done."
echo
echo "Next commands:"
echo "  dotnet restore src/${product_name}.sln"
echo "  dotnet build src/${product_name}.sln"
echo "  dotnet test src/${product_name}.sln"
echo "  docker compose -f docker-compose.postgres.yml up -d"
