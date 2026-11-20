ALTER TABLE ProcessingSession ADD COLUMN ProductionTiffBackend TEXT NOT NULL DEFAULT 'PHOTOSHOP'
    CHECK (ProductionTiffBackend IN ('PHOTOSHOP', 'MAGICK_NET'));
