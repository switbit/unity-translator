# Сторонние компоненты

Проект использует и/или распространяет следующие сторонние компоненты.

## BepInEx 5

- Сайт: https://github.com/BepInEx/BepInEx
- Лицензия: LGPL-2.1

Установщик `UnityTranslator-Setup.exe` содержит архивы BepInEx 5.4.23.6 для x64 и x86.
Исходный код BepInEx доступен по ссылке выше. При распространении установщика
сохраняется уведомление о лицензии; BepInEx распространяется «как есть», без гарантий.

## HarmonyX

- Сайт: https://github.com/BepInEx/HarmonyX
- Лицензия: MIT

Используется для патчинга методов Unity-игр. Входит в состав BepInEx 5.

## TextMeshPro

- Пакет Unity: `com.unity.textmeshpro`
- Лицензия: Unity Companion License

Проект не распространяет сборки TextMeshPro. Плагин использует API TextMeshPro,
которые уже присутствуют в игре.

## Google Translate

- Сайт: https://translate.google.com
- Лицензия/условия: https://policies.google.com/terms

Неофициальный веб-эндпоинт используется как провайдер перевода по умолчанию.
Проект не связан с Google. При высокой нагрузке возможны ограничения (HTTP 429).

## LibreTranslate

- Сайт: https://github.com/LibreTranslate/LibreTranslate
- Лицензия: AGPL-3.0

Опциональный провайдер. Проект не включает LibreTranslate в свои сборки и
обращается только к API сервера, указанного пользователем.
