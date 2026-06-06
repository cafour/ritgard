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
export UV_HTTP_TIMEOUT=600

uv sync --no-install-package flash-attn

datasets=${datasets:-$(ls -1 './datasets' | grep -P '[^\.]+\.json' | grep -v -e 'positions' -e 'terrain' | sed 's/.json//')}

extension='.json'

if [[ "$suffix" != '' ]]; then
    extension=".$suffix.json"
fi

topics_extension='.json'

if [[ "$topics_suffix" != '' ]]; then
    topics_extension=".$topics_suffix.json"
fi

for dataset in $datasets; do
    args=()
    args+=(--world-sizing "${world_sizing:-auto}")
    if [[ "$world_scale" != '' ]]; then
        args+=(--world-scale "$world_scale")
    fi
    
    if [[ "$auto_quantile" != '' ]]; then
        args+=(--world-sizing-auto-quantile "$auto_quantile")
    fi

    args+=(--data-path "./datasets/$dataset.json")
    args+=(--output ./out/$dataset-positions$extension)
    args+=(./datasets/modeling/topics_$dataset$topics_extension)

    echo "Adjusting dataset '$dataset'"
    uv run --no-sync ./adjust-positions.py "${args[@]}" &
    # echo uv run --no-sync ./adjust-positions.py "${args[@]}" &
done

wait
cp out/* /storage/brno2/home/xstepan1/out/positions

rm -rf $SCRATCHDIR/*
