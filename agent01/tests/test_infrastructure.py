"""
Tests for infrastructure layer (audio, cache, io).
"""
import pytest
from agent01.infrastructure import AudioUtils, CacheManager


class TestAudioUtils:
    """Tests for AudioUtils."""
    
    def test_format_mb(self):
        """Test MB formatting."""
        assert AudioUtils.format_mb(1024 * 1024) == "1.00 MB"
        assert AudioUtils.format_mb(5 * 1024 * 1024) == "5.00 MB"
    
    def test_calculate_segment_time(self):
        """Test segment time calculation."""
        # 100MB file, 1000 seconds, target 10MB
        result = AudioUtils.calculate_segment_time(100_000_000, 1000, 10)
        assert result > 0
        assert isinstance(result, int)


class TestCacheManager:
    """Tests for CacheManager."""
    
    def test_create_cache_manager(self):
        """Test creating cache manager."""
        cache = CacheManager("test_cache")
        assert cache.cache_dir == "test_cache"
    
    # Add more tests when implementing actual cache logic

