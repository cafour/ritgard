#!/bin/bash

#PBS -N ritgard-positions
#PBS -l select=1:ncpus=10:mem=20gb:scratch_local=40gb
#PBS -l walltime=2:00:00

set -o errexit

if ! command -v uv >/dev/null 2>&1 ; then
	curl -LsSf https://astral.sh/uv/install.sh | sh
fi

source $HOME/.local/bin/env

cp -r /storage/brno2/home/xstepan1/ritgard $SCRATCHDIR

cd $SCRATCHDIR/ritgard/data-processing
mkdir -p datasets

export HF_HUB_DISABLE_XET=1

uv sync --preview-features extra-build-dependencies --no-build-isolation-package flash-attn
