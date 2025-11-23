#!/usr/bin/env python3
"""
Setup script for agent03 package.
"""
from setuptools import setup
from pathlib import Path

# Read README
readme_file = Path(__file__).parent / "README.md"
long_description = ""
if readme_file.exists():
    long_description = readme_file.read_text(encoding="utf-8")

setup(
    name="agent03",
    version="3.0.0",
    author="Agent03 Team",
    author_email="",
    description="Modular transcription system with OpenAI API",
    long_description=long_description,
    long_description_content_type="text/markdown",
    packages=[
        "core",
        "services",
        "infrastructure",
        "infrastructure.audio",
        "infrastructure.cache",
        "infrastructure.io",
        "cli",
    ],
    package_dir={"": "."},
    classifiers=[
        "Development Status :: 4 - Beta",
        "Intended Audience :: Developers",
        "Topic :: Multimedia :: Sound/Audio :: Speech",
        "Programming Language :: Python :: 3",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
    ],
    python_requires=">=3.8",
    install_requires=[
        "openai>=1.0.0",
        "python-dotenv>=0.19.0",
        "pyannote.audio>=3.0.0",
        "pydub>=0.25.0",
        "torchaudio>=2.0.0",
    ],
    extras_require={
        "dev": [
            "pytest>=7.0",
            "pytest-cov>=4.0",
            "black>=23.0",
            "flake8>=6.0",
            "mypy>=1.0",
        ],
    },
    entry_points={
        "console_scripts": [
            "agent03=cli.main:main",
        ],
    },
    include_package_data=True,
    package_data={
        "": ["*.json"],
    },
)
