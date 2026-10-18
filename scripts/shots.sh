#!/bin/zsh
# Renders screenshots of the real game (needs a display). Usage: scripts/shots.sh <outdir> [name...]
OUT=${1:-shots}; shift
B="$(dirname $0)/../src/Portal2D/bin/Debug/net10.0/Portal2D"
mkdir -p $OUT
typeset -A S
S=(menu "menu 1.5" select "select 1.2" options "options 0.6" controls "controls 0.6" pause "pause 0.6" complete "complete 1.2"
   l1 "level:1 4.2 solve" l4 "level:4 3.6 solve" l6 "level:6 3.0 solve" l8 "level:8 5.0 solve" l9 "level:9 5.9 solve" l12 "level:12 8.5 solve" l1i "level:1 1.0" l13 "level:13 6.0 solve" l15 "level:15 4.6 solve" l17 "level:17 3.0 solve" r1 "random:42 6.0 solve" r2 "random:42 1.0")
for n in ${@:-${(k)S}}; do
  a=(${=S[$n]})
  $B --shot $a[1] $a[2] $OUT/$n.png $a[3] >/dev/null 2>&1 || echo "failed $n"
done
echo done
