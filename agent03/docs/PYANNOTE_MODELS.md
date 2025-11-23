# 🎯 Модели pyannote.audio

## Цепочка зависимостей

Для работы диаризации через `pyannote/speaker-diarization-3.1` требуется доступ к **трем** моделям:

```
speaker-diarization-3.1
    ├── speaker-diarization-community-1
    │   └── segmentation-3.0
    └── (другие компоненты)
```

### 1. speaker-diarization-3.1
- **URL:** https://huggingface.co/pyannote/speaker-diarization-3.1
- **Роль:** Основная pipeline диаризации
- **Лицензия:** MIT

### 2. speaker-diarization-community-1
- **URL:** https://huggingface.co/pyannote/speaker-diarization-community-1
- **Роль:** Улучшенная community версия (используется внутри 3.1)
- **Лицензия:** CC-BY-4.0
- **Особенность:** Показывает лучшие результаты чем 3.1 в бенчмарках

### 3. segmentation-3.0
- **URL:** https://huggingface.co/pyannote/segmentation-3.0
- **Роль:** Базовая модель сегментации речи
- **Лицензия:** MIT

## Производительность

По данным [pyannote.audio](https://huggingface.co/pyannote/speaker-diarization-community-1), `community-1` показывает лучшие результаты:

| Датасет | speaker-diarization-3.1 | community-1 |
|---------|-------------------------|-------------|
| AMI (IHM) | 18.8% | 17.0% |
| DIHARD 3 | 21.4% | 20.2% |
| VoxConverse | 11.2% | 11.2% |

*(Чем меньше DER (Diarization Error Rate), тем лучше)*

## Альтернатива: Использовать community-1 напрямую

Вы можете использовать `community-1` напрямую для лучшей производительности:

```python
# В файле: infrastructure/audio/diarizer.py
# Измените модель по умолчанию в core/config.py:

defaults = {
    # ...
    "diarization_model": "pyannote/speaker-diarization-community-1",  # Вместо 3.1
    # ...
}
```

**Преимущества:**
- ✅ Лучшая точность (меньше DER)
- ✅ Эксклюзивная диаризация (легче совмещать с транскрипцией)
- ✅ Тот же API

**Недостатки:**
- ⚠️ Все еще требует доступ ко всем трем моделям

## Почему модели "gated"?

Все три модели требуют принятия условий использования:
- Защита интеллектуальной собственности
- Сбор статистики использования
- Соблюдение лицензионных требований

После одобрения доступ остается постоянным для вашего токена.

---

**v3.0.0** 🎯

