# Corpus MusicXML

Archivos con los que se mide la fidelidad de ida y vuelta de MusicXML (F3.1).

- `public/`: MusicXML Test Suite (fork de la suite de Lilypond, ahora en el W3C Music Notation
  Community Group), <https://github.com/w3c-cg/musicxmlTestSuite>, commit `77c19f7`.
  Licencia MIT (`public/LICENSE.txt`): hay que conservar el aviso de copyright junto a los archivos.
- `real/dorico/`, `real/sibelius/`, `real/musescore/`: archivos reales exportados desde cada programa.
  Están vacíos: los tiene que aportar el propietario (Tessitura no puede generarlos). Cada archivo
  nuevo aparece solo en el informe; conviene guardar junto a él un `.txt` con la versión del programa.
  Solo se admiten archivos cuya licencia permita distribuirlos en el repositorio.
