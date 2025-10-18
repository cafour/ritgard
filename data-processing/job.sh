#!/bin/bash

#PBS -N ritgard
#PBS -l select=1:ncpus=4:mem=40gb:scratch_local=40gb:ngpus=1:gpu_mem=50gb
#PBS -l walltime=1:00:00

set -o errexit

curl -LsSf https://astral.sh/uv/install.sh | sh

cp -r /storage/brno2/home/xstepan1/ritgard $SCRATCHDIR

cd $SCRATCHDIR/ritgard/data-processing
mkdir -p datasets

uv sync --preview-features extra-build-dependencies --no-build-isolation-package flash-attn
uv run ./model-topics.py --llm --embed-labels --embed-bodies --embed-comments --embed-model "Qwen/Qwen3-Embedding-8B" --flash-attention ./datasets/lume.json
cp out/* /storage/brno2/home/xstepan1/out/

rm -rf $SCRATCHDIR/*
