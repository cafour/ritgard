#!/bin/bash

#PBS -N ritgard-positions
#PBS -l select=1:ncpus=14:mem=40gb:scratch_local=40gb
#PBS -l walltime=4:00:00

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

out_dir=${out_dir:-/storage/brno2/home/xstepan1/out/positions}

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
    out_filename="$dataset-positions$extension"
    if [[ -f "$out_dir/$out_filename" ]]; then
        echo "Skipping '$dataset' because output '$out_filename' already exists"
	continue
    fi

    args=()
    args+=(--world-sizing "${world_sizing:-auto}")
    if [[ "$world_scale" != '' ]]; then
        args+=(--world-scale "$world_scale")
    fi
    
    if [[ "$auto_quantile" != '' ]]; then
        args+=(--world-sizing-auto-quantile "$auto_quantile")
    fi

    args+=(--data-path "./datasets/$dataset.json")
    args+=(--output ./out/$out_filename)
    args+=(./datasets/modeling/topics_$dataset$topics_extension)

    echo "Adjusting dataset '$dataset'"
    (uv run --no-sync ./adjust-positions.py "${args[@]}" && cp ./out/$out_filename $out_dir/) &
    # echo uv run --no-sync ./adjust-positions.py "${args[@]}" &
done

wait
cp out/* $out_dir

rm -rf $SCRATCHDIR/*
