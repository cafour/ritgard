#!/bin/bash

#PBS -N ritgard
#PBS -l select=1:ncpus=4:mem=40gb:scratch_local=40gb:ngpus=1:gpu_mem=50gb
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
export UV_HTTP_TIMEOUT=600

uv sync --preview-features extra-build-dependencies --no-build-isolation-package flash-attn
model_args=(--llm)
if [[ "${embed_labels:-1}" == "1" ]]; then
	model_args+=(--embed-labels)
fi
if [[ "${embed_bodies:-1}" == "1" ]]; then
	model_args+=(--embed-bodies)
fi
if [[ "${embed_comments:-1}" == "1" ]]; then
	model_args+=(--embed-comments)
fi

model_args+=(--embed-model "Qwen/Qwen3-Embedding-8B")
model_args+=(--embed-batch-size 1)
model_args+=(--min-cluster-size "${min_cluster_size:-10}")
model_args+=(--min-samples "${min_samples:-5}")
model_args+=(--flash-attention)
model_args+=("./datasets/$dataset.json")
uv run ./model-topics.py "${model_args[@]}" ${extra_args}
cp out/* /storage/brno2/home/xstepan1/out/

rm -rf $SCRATCHDIR/*
