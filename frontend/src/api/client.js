import axios from 'axios'
import { acquireApiAccessToken } from '../auth/acquireApiToken'
import { isAzureAuthConfigured } from '../auth/authConfig'

export const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

api.interceptors.request.use(async (config) => {
  if (isAzureAuthConfigured()) {
    const token = await acquireApiAccessToken()
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
  }
  return config
})

api.interceptors.response.use(
  (r) => r,
  (err) => {
    if (err.response?.status === 401 && isAzureAuthConfigured()) {
      window.location.assign(window.location.origin)
    }
    return Promise.reject(err)
  },
)
