#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

function usage() {
  cat <<EOF
Usage: $(basename "$0") [options] [-- <dotnet publish options>]

Options:
  -r, --runtime <RID>       Target runtime (default: linux-x64)
  -o, --output <DIRECTORY>  Output directory (default: ${SCRIPT_DIR}/setup)
      --file-name <NAME>    Executable file name (default: setup)
  -h, --help                Show this help
EOF
}

function fail() {
  printf 'Error: %s\n' "$*" >&2
  usage >&2
  exit 2
}

runtime="linux-x64"
output="${SCRIPT_DIR}/artifacts"
file_name="ScriptCommandRunner"
dotnet_arguments=()

while [[ $# -gt 0 ]]; do
  case $1 in
    -r|--runtime)
      [[ $# -ge 2 ]] || fail "$1 requires a value"
      runtime=$2
      shift 2
      ;;
    --runtime=*)
      runtime="${1#*=}"
      shift
      ;;
    -o|--output)
      [[ $# -ge 2 ]] || fail "$1 requires a value"
      output=$2
      shift 2
      ;;
    --output=*)
      output="${1#*=}"
      shift
      ;;
    --file-name)
      [[ $# -ge 2 ]] || fail "$1 requires a value"
      file_name=$2
      shift 2
      ;;
    --file-name=*)
      file_name="${1#*=}"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --)
      shift
      dotnet_arguments=("$@")
      break
      ;;
    *)
      fail "Unknown option: $1"
      ;;
  esac
done

[[ -n "${runtime}" ]] || fail "--runtime must not be empty"
[[ -n "${output}" ]] || fail "--output must not be empty"
[[ -n "${file_name}" ]] || fail "--file-name must not be empty"
[[ "${file_name}" != */* && "${file_name}" != *\\* ]] || fail "--file-name must not contain path separators"

if [[ -e "${output}" && ! -d "${output}" ]]; then
  fail "--output must be a directory: ${output}"
fi

mkdir -p "${output}"
output="$(cd "${output}" && pwd -P)"
publish_directory="$(mktemp -d)"

function cleanup() {
  [[ -n "${publish_directory:-}" && -d "${publish_directory}" ]] || return
  rm -rf -- "${publish_directory}"
}

trap cleanup EXIT

dotnet publish \
  "${SCRIPT_DIR}/src/ScriptCommandRunner/ScriptCommandRunner.csproj" \
  --configuration Release \
  --runtime "${runtime}" \
  --output "${publish_directory}" \
  -p:AssemblyName="${file_name}" \
  -p:DebugSymbols=false \
  "${dotnet_arguments[@]}"

cp -R "${publish_directory}/." "${output}/"
printf 'Published: %s\n' "${output}"
