#!/usr/bin/env python3
"""
Практические примеры использования модулей agent03 для внешних агентов.
"""
import sys
import os

# Добавить родительскую папку в path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


# ============================================================================
# ПРИМЕР 1: Полный pipeline с конфигурацией
# ============================================================================
def example_full_pipeline():
    """Полный pipeline обработки аудио файла."""
    from agent03 import Config, TranscriptionPipeline
    
    # Загрузить конфигурацию
    config = Config.from_file("agent03/config.json")
    
    # Показать план выполнения
    config.print_plan("agent03/config.json")
    
    # Создать pipeline
    pipeline = TranscriptionPipeline(config)
    
    # Обработать все файлы из конфига
    results = pipeline.process_all_files()
    
    # Вывести результаты
    for md_path, json_path in results:
        print(f"✓ Markdown: {md_path}")
        print(f"✓ JSON: {json_path}")


# ============================================================================
# ПРИМЕР 2: Только разделение на чанки (без транскрибации)
# ============================================================================
def example_chunking_only():
    """Разделить большой аудио файл на части с перекрытием."""
    from agent03 import AudioChunker
    
    chunker = AudioChunker(
        ffmpeg_path="ffmpeg",
        ffprobe_path="ffprobe"
    )
    
    # Разделить файл
    chunks = chunker.process_chunks_for_file(
        source_path="voice 1.m4a",
        target_mb=5.0,
        workdir="chunks",
        naming_pattern="{base}_part_%03d.m4a",
        overlap_sec=2.0,
        reencode=True,
        reencode_bitrate=64
    )
    
    # Вывести информацию о чанках
    for i, chunk in enumerate(chunks):
        print(f"Chunk {i}:")
        print(f"  Path: {chunk.path}")
        print(f"  Offset: {chunk.offset:.2f}s")
        print(f"  Emit guard: {chunk.emit_guard:.2f}s")


# ============================================================================
# ПРИМЕР 3: Только транскрибация (без chunking)
# ============================================================================
def example_transcription_only():
    """Транскрибировать аудио файл напрямую через API."""
    from agent03 import OpenAITranscriptionClient
    import os
    
    # Создать клиент
    client = OpenAITranscriptionClient(
        api_key=os.getenv("OPENAI_API_KEY"),
        model="gpt-4o-transcribe-diarize"
    )
    
    # Транскрибировать
    response = client.transcribe(
        audio_path="audio.m4a",
        language=None,  # auto-detect
        temperature=0.0
    )
    
    # Парсить сегменты
    segments = client.parse_segments(response)
    
    # Вывести результаты
    for seg in segments:
        speaker = seg.speaker or "unknown"
        print(f"[{seg.start:.2f}s - {seg.end:.2f}s] {speaker}: {seg.text}")


# ============================================================================
# ПРИМЕР 4: Работа с кешем
# ============================================================================
def example_cache_management():
    """Управление кешем транскрипций."""
    from agent03 import CacheManager
    
    cache = CacheManager(cache_dir="cache")
    
    # Получить fingerprint файла
    audio_file = "audio.m4a"
    fingerprint = cache.get_file_fingerprint(audio_file)
    print(f"File fingerprint: {fingerprint[:16]}...")
    
    # Загрузить манифест
    manifest_path = cache.get_manifest_path("audio")
    manifest = cache.load_manifest(manifest_path)
    
    # Проверить кеш для чанка
    chunk_name = "audio_part_001.m4a"
    cached_response = cache.get_cached_response(
        manifest, chunk_name, fingerprint
    )
    
    if cached_response:
        print(f"Found cached response for {chunk_name}")
    else:
        print(f"No cache for {chunk_name}, need to transcribe")
        
        # Симуляция: сохранить в кеш
        fake_response = {"text": "Hello world", "segments": []}
        cache.cache_response(
            manifest, manifest_path,
            chunk_name, fingerprint, fake_response
        )
        print(f"Cached response for {chunk_name}")


# ============================================================================
# ПРИМЕР 5: Форматирование вывода
# ============================================================================
def example_output_formatting():
    """Создать Markdown и JSON вывод из сегментов."""
    from agent03 import OutputWriter, ASRSegment
    
    writer = OutputWriter()
    
    # Создать тестовые сегменты
    segments = [
        ASRSegment(0.0, 2.5, "Привет, как дела?", "speaker_A"),
        ASRSegment(2.5, 5.0, "Отлично, спасибо!", "speaker_B"),
        ASRSegment(5.0, 8.5, "Расскажи о своём дне.", "speaker_A"),
        ASRSegment(8.5, 12.0, "Сегодня был продуктивный день.", "speaker_B"),
    ]
    
    # Создать Markdown файл
    md_path = "example_output.md"
    writer.initialize_markdown(md_path)
    writer.append_segments_to_markdown(
        md_path=md_path,
        segments=segments,
        offset=0.0,
        emit_guard=0.0
    )
    writer.finalize_markdown(md_path)
    
    print(f"Created markdown: {md_path}")


# ============================================================================
# ПРИМЕР 6: Кастомная конфигурация программно
# ============================================================================
def example_custom_config():
    """Создать конфигурацию программно (без JSON файла)."""
    from agent03 import Config, TranscriptionPipeline
    
    # Создать конфигурацию из словаря
    config_dict = {
        "file": "audio.m4a",
        "model": "gpt-4o-transcribe-diarize",
        "openai_api_key": os.getenv("OPENAI_API_KEY"),
        "pre_split": False,  # Отключить chunking
        "md_output_path": "custom_transcript.md",
        "raw_json_output_path": "custom_response.json",
        "cache_dir": "custom_cache",
        "temperature": 0.2,
    }
    
    config = Config(config_dict)
    
    # Использовать в pipeline
    pipeline = TranscriptionPipeline(config)
    md_path, json_path = pipeline.process_file("audio.m4a")
    
    print(f"Processed with custom config:")
    print(f"  Markdown: {md_path}")
    print(f"  JSON: {json_path}")


# ============================================================================
# ПРИМЕР 7: Обработка нескольких файлов последовательно
# ============================================================================
def example_batch_processing():
    """Обработать несколько файлов последовательно."""
    from agent03 import Config, TranscriptionPipeline
    
    # Список файлов для обработки
    files = ["audio1.m4a", "audio2.m4a", "audio3.m4a"]
    
    config_dict = {
        "files": files,  # Множественные файлы
        "model": "gpt-4o-transcribe-diarize",
        "openai_api_key": os.getenv("OPENAI_API_KEY"),
        "pre_split": True,
        "target_chunk_mb": 10,
        "cache_dir": "batch_cache",
    }
    
    config = Config(config_dict)
    pipeline = TranscriptionPipeline(config)
    
    # Обработать все файлы
    results = pipeline.process_all_files()
    
    print(f"Processed {len(results)} files")
    for md_path, json_path in results:
        print(f"  ✓ {md_path}")


# ============================================================================
# ПРИМЕР 8: Минимальный пример для быстрого старта
# ============================================================================
def example_quick_start():
    """Самый простой способ транскрибировать файл."""
    from agent03 import Config, TranscriptionPipeline
    import os
    
    # Минимальная конфигурация
    config = Config({
        "file": "audio.m4a",
        "openai_api_key": os.getenv("OPENAI_API_KEY"),
    })
    
    # Запустить
    pipeline = TranscriptionPipeline(config)
    md_path, json_path = pipeline.process_file("audio.m4a")
    
    print(f"Done! Check {md_path}")


# ============================================================================
# ПРИМЕР 9: Использование отдельных утилит
# ============================================================================
def example_audio_utils():
    """Использовать аудио утилиты для анализа файлов."""
    from agent03 import AudioUtils
    
    audio_file = "audio.m4a"
    
    # Получить информацию о файле
    duration, size = AudioUtils.get_duration_and_size("ffprobe", audio_file)
    
    print(f"Audio info:")
    print(f"  Duration: {duration:.2f}s")
    print(f"  Size: {AudioUtils.format_mb(size)}")
    
    # Рассчитать оптимальный размер сегмента
    target_mb = 5.0
    segment_time = AudioUtils.calculate_segment_time(size, duration, target_mb)
    print(f"  Optimal segment: {segment_time}s for {target_mb}MB chunks")


# ============================================================================
# ПРИМЕР 10: Интеграция с внешним агентом (callback)
# ============================================================================
def example_external_agent_integration():
    """Пример интеграции с внешним агентом через callbacks."""
    from agent03 import Config, TranscriptionPipeline
    import os
    
    class ExternalAgent:
        """Симуляция внешнего агента."""
        
        def on_chunk_processed(self, chunk_index, total_chunks, segments):
            """Callback вызывается после обработки каждого чанка."""
            print(f"\n[Agent] Processed chunk {chunk_index+1}/{total_chunks}")
            print(f"[Agent] Got {len(segments)} segments")
            
            # Агент может обрабатывать результаты в реальном времени
            for seg in segments[:3]:  # Показать первые 3
                print(f"  {seg.speaker}: {seg.text[:50]}...")
        
        def on_file_complete(self, md_path, json_path):
            """Callback вызывается после завершения файла."""
            print(f"\n[Agent] File complete!")
            print(f"[Agent] Markdown: {md_path}")
            print(f"[Agent] JSON: {json_path}")
    
    # Создать агента
    agent = ExternalAgent()
    
    # Настроить pipeline (можно расширить для поддержки callbacks)
    config = Config({
        "file": "audio.m4a",
        "openai_api_key": os.getenv("OPENAI_API_KEY"),
        "pre_split": True,
        "target_chunk_mb": 5,
    })
    
    pipeline = TranscriptionPipeline(config)
    
    # Обработать (в реальной реализации добавить поддержку callbacks)
    md_path, json_path = pipeline.process_file("audio.m4a")
    agent.on_file_complete(md_path, json_path)


# ============================================================================
# Главная функция для запуска примеров
# ============================================================================
def main():
    """Запустить выбранный пример."""
    import sys
    
    examples = {
        "1": ("Полный pipeline", example_full_pipeline),
        "2": ("Только chunking", example_chunking_only),
        "3": ("Только транскрибация", example_transcription_only),
        "4": ("Работа с кешем", example_cache_management),
        "5": ("Форматирование вывода", example_output_formatting),
        "6": ("Кастомная конфигурация", example_custom_config),
        "7": ("Batch обработка", example_batch_processing),
        "8": ("Быстрый старт", example_quick_start),
        "9": ("Аудио утилиты", example_audio_utils),
        "10": ("Интеграция с агентом", example_external_agent_integration),
    }
    
    print("Доступные примеры:")
    print("=" * 60)
    for key, (name, _) in examples.items():
        print(f"  {key}. {name}")
    print("=" * 60)
    
    if len(sys.argv) > 1:
        choice = sys.argv[1]
    else:
        choice = input("\nВыберите пример (1-10): ").strip()
    
    if choice in examples:
        name, func = examples[choice]
        print(f"\n🚀 Запуск примера: {name}\n")
        print("=" * 60)
        try:
            func()
        except Exception as e:
            print(f"\n❌ Ошибка: {e}")
            import traceback
            traceback.print_exc()
    else:
        print(f"❌ Неверный выбор: {choice}")


if __name__ == "__main__":
    main()

