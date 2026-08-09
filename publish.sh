#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

function usage() {
  cat <<EOF
Usage: $(basename "$0") [options] [-- <dotnet publish options>]

Options:
  -r, --runtime <RID>       Target runtime (default: linux-x64)
  -o, --output <DIRECTORY>  Output directory (default: ${SCRIPT_DIR}/artifacts)
      --file-name <NAME>    Executable file name (default: ScriptCommandRunner)
      --clean               Remove existing files in the output directory before copying
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
clean=false
dotnet_arguments=()

while [[ $# -gt 0 ]]; do
  case $1 in
    --*=*)
      # Normalize --option=value to --option value and reprocess.
      set -- "${1%%=*}" "${1#*=}" "${@:2}"
      ;;
    -r|--runtime)
      [[ $# -ge 2 ]] || fail "$1 requires a value"
      runtime=$2
      shift 2
      ;;
    -o|--output)
      [[ $# -ge 2 ]] || fail "$1 requires a value"
      output=$2
      shift 2
      ;;
    --file-name)
      [[ $# -ge 2 ]] || fail "$1 requires a value"
      file_name=$2
      shift 2
      ;;
    --clean)
      clean=true
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
trap 'rm -rf -- "${publish_directory}"' EXIT

dotnet publish \
  "${SCRIPT_DIR}/src/ScriptCommandRunner/ScriptCommandRunner.csproj" \
  --configuration Release \
  --runtime "${runtime}" \
  --output "${publish_directory}" \
  -p:AssemblyName="${file_name}" \
  -p:DebugSymbols=false \
  ${dotnet_arguments[@]+"${dotnet_arguments[@]}"}

# PublishAot emits separate native symbols (.dbg on Linux, .pdb on Windows,
# .dSYM on macOS) that DebugSymbols=false does not suppress.
rm -rf -- \
  "${publish_directory}/${file_name}.dbg" \
  "${publish_directory}/${file_name}.pdb" \
  "${publish_directory}/${file_name}.dSYM"

if [[ "${clean}" == true ]]; then
  find "${output}" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
fi

cp -R "${publish_directory}/." "${output}/"
printf 'Published: %s\n' "${output}"
