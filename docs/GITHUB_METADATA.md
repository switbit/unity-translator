# Метаданные для GitHub

## About (краткое описание репозитория)

> Универсальный переводчик текста для Unity-игр на Mono: сам определяет язык, переводит интерфейс и катсцены прямо в игре. BepInEx 5 + Harmony, TextMeshPro, кэш и горячая клавиша F5.

## Topics

`unity`, `unity3d`, `bepinex`, `harmony`, `translation`, `localization`, `game-mod`, `textmeshpro`, `csharp`, `windows`

## Release title

`v1.0.0 — первый публичный релиз`

## Release notes

Текст заметок хранится в [`release-notes-v1.0.0.md`](release-notes-v1.0.0.md) и публикуется как тело релиза:

```powershell
gh release create v1.0.0 --title "v1.0.0 — первый публичный релиз" `
  --notes-file docs\release-notes-v1.0.0.md `
  dist\UnityTranslator-Setup.exe dist\UnityTranslator-1.0.0.zip dist\SHA256SUMS.txt
```

## Проверка релиза

- [x] Прикрепить `UnityTranslator-Setup.exe`
- [x] Прикрепить `UnityTranslator-1.0.0.zip`
- [x] Прикрепить `SHA256SUMS.txt`
- [x] Проверить ссылки в README
- [x] Проверить, что в репозиторий не попали `dist/`, `.cache/`, `obj/`
- [x] Проверить, что в исходниках нет локальных путей и токенов
