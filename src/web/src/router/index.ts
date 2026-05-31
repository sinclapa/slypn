import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import AboutView from '@/views/AboutView.vue'
import ArticlesView from '@/views/ArticlesView.vue'
import ArticleDetailView from '@/views/ArticleDetailView.vue'
import BlogView from '@/views/BlogView.vue'
import EventsView from '@/views/EventsView.vue'
import ResourcesView from '@/views/ResourcesView.vue'
import NewsletterView from '@/views/NewsletterView.vue'
import LoginView from '@/views/LoginView.vue'
import AuthCallbackView from '@/views/AuthCallbackView.vue'
import NotFoundView from '@/views/NotFoundView.vue'

export default createRouter({
  history: createWebHistory(),
  scrollBehavior: () => ({ top: 0 }),
  routes: [
    { path: '/',                  name: 'home',            component: HomeView },
    { path: '/about',             name: 'about',           component: AboutView },
    { path: '/articles',          name: 'articles',        component: ArticlesView },
    { path: '/articles/:slug',    name: 'article-detail',  component: ArticleDetailView },
    { path: '/blog',              name: 'blog',            component: BlogView },
    { path: '/events',            name: 'events',          component: EventsView },
    { path: '/resources',         name: 'resources',       component: ResourcesView },
    { path: '/newsletter',        name: 'newsletter',      component: NewsletterView },
    { path: '/login',             name: 'login',           component: LoginView },
    { path: '/auth/callback',     name: 'auth-callback',   component: AuthCallbackView },
    { path: '/:pathMatch(.*)*',   name: 'not-found',       component: NotFoundView },
  ],
})
