#!/bin/bash

#PBS -N ritgard
#PBS -l select=1:ncpus=4:mem=40gb:scratch_local=40gb:ngpus=1:gpu_mem=40gb
#PBS -l walltime=1:00:00

set -o errexit

curl -LsSf https://astral.sh/uv/install.sh | sh

cd $SCRATCHDIR

git clone git@gitlab.com:helveg/ritgard.git

cd ritgard/data-processing
cp /storage/brno2/home/xstepan1/ritgard/data-processing/.env .
mkdir datasets
cp /storage/brno2/home/xstepan1/ritgard/data-processing/datasets/lume.json ./datasets/

uv sync
uv run ./model-topics.py --llm --llm-model --embed-labels --embed-bodies --embed-comments --embed-model "Qwen/Qwen3-Embedding-8B" --flash-attention
cp out/* /storage/brno2/home/xstepan1/out/

rm -rf $SCRATCHDIR/*
