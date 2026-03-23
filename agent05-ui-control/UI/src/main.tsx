import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './index.css';
import { refinerUiDebug } from './utils/refinerUiDebug';

refinerUiDebug(
  'flag on — refiner/SSE logs use console.log; filter Console by text [refiner-ui]'
);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
